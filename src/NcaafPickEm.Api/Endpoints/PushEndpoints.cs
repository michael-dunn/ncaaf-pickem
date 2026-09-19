using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Contracts.Push;
using NcaafPickEm.Shared.Enums;
using PushSubscriptionEntity = NcaafPickEm.Domain.Notifications.PushSubscription;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Web push subscriptions and the commissioner's notification log (Feature 11, P7-01).
/// </summary>
/// <remarks>
/// Everything here is per-device: the browser owns the endpoint URL, and the same person on a
/// phone and a laptop has two subscriptions. The subscribe route is an upsert keyed on that
/// endpoint, so a client that re-subscribes (a permission toggle, a reinstall, a new session on a
/// shared device) never leaves a second row behind.
/// </remarks>
public static class PushEndpoints
{
    /// <summary>How many log rows the commissioner's view returns, newest first.</summary>
    public const int LogRowLimit = 200;

    /// <summary>
    /// The week a Development test push is recorded against, so it can never be mistaken for a
    /// real week's notification.
    /// </summary>
    private const int TestPushWeek = 0;

    /// <summary>
    /// Maps <c>/api/push/*</c> and <c>/api/leagues/{leagueId}/notifications/log</c>, plus the
    /// Development-only test push.
    /// </summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapPushEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder push = api.MapGroup("/push")
            .WithTags("push")
            .RequireAuthorization(PolicyNames.Authenticated);

        push.MapGet("/vapid-public-key", GetVapidPublicKey).WithName("PushVapidPublicKey");

        push.MapPost("/subscriptions", SubscribeAsync)
            .WithName("PushSubscribe")
            .AddEndpointFilter<ValidationFilter<PushSubscriptionRequest>>();

        push.MapDelete("/subscriptions", UnsubscribeAsync)
            .WithName("PushUnsubscribe")
            .AddEndpointFilter<ValidationFilter<DeletePushSubscriptionRequest>>();

        push.MapGet("/status", GetStatusAsync).WithName("PushStatus");

        // The commissioner's proof that reminders went out (Feature 11 delivery criteria). It
        // lives here rather than in LeagueEndpoints because the rows and their meaning belong to
        // this feature.
        api.MapGroup("/leagues/{leagueId:guid}/notifications")
            .WithTags("push")
            .RequireLeagueCommissioner()
            .MapGet("/log", GetLogAsync)
            .WithName("NotificationLog");

        // P7-02 needs a way to prove a real device is wired up end to end. Commissioner-only and
        // Development/Testing-only: it sends to the caller's own devices and nobody else's.
        var environment = ((IEndpointRouteBuilder)api).ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            push.MapPost("/test", SendTestAsync)
                .WithName("PushTest")
                .RequireAnyLeagueCommissioner();
        }

        return api;
    }

    private static Results<Ok<VapidPublicKeyResponse>, ProblemHttpResult> GetVapidPublicKey(
        VapidConfiguration vapid)
    {
        if (vapid.PublicKey is not string publicKey)
        {
            // 503, not 500: the server is healthy, this one capability is switched off until an
            // operator sets Push__*. The client shows "notifications unavailable" and stops.
            return TypedResults.Problem(
                title: "Push notifications are not configured.",
                detail: vapid.Error,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return TypedResults.Ok(new VapidPublicKeyResponse(publicKey));
    }

    private static async Task<NoContent> SubscribeAsync(
        PushSubscriptionRequest request,
        ICurrentUser currentUser,
        AppDbContext database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        byte[] endpointHash = PushEndpointHash.Compute(request.Endpoint);

        // The unique index is on the computed EndpointHash column, not on Endpoint (D-017), so the
        // lookup hashes in C# exactly the way SQL Server does and seeks. PushSubscriptionTests
        // asserts the two hashes agree against a real database.
        PushSubscriptionEntity? existing = await database.PushSubscriptions
            .FirstOrDefaultAsync(subscription => subscription.EndpointHash == endpointHash, cancellationToken);

        if (existing is null)
        {
            database.PushSubscriptions.Add(new PushSubscriptionEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = currentUser.UserId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                UserAgent = request.UserAgent,
                CreatedUtc = timeProvider.GetUtcNow().UtcDateTime,
            });
        }
        else
        {
            // Re-owning is deliberate: a browser endpoint belongs to whoever is signed in on that
            // device now. A shared family tablet that signs out and back in as somebody else must
            // not keep pushing the previous member's reminders to it.
            existing.UserId = currentUser.UserId;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.UserAgent = request.UserAgent;
            existing.FailureCount = 0;
        }

        await database.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> UnsubscribeAsync(
        // Minimal APIs never infer a body for DELETE, and 03-API-Contracts.md specifies one here
        // (the endpoint URL is far too long to be a route value and too identifying for a log).
        [FromBody] DeletePushSubscriptionRequest request,
        ICurrentUser currentUser,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        byte[] endpointHash = PushEndpointHash.Compute(request.Endpoint);

        Guid subscriptionId = await database.PushSubscriptions
            .Where(subscription => subscription.EndpointHash == endpointHash
                && subscription.UserId == currentUser.UserId)
            .Select(subscription => subscription.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Idempotent: deleting something that is already gone, or something another account owns,
        // is a 204 either way. The client only ever wants "this device is off" to end up true.
        if (subscriptionId != Guid.Empty)
        {
            await PushDelivery.RemoveSubscriptionsAsync(database, [subscriptionId], cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<PushStatusResponse>> GetStatusAsync(
        string? endpoint,
        ICurrentUser currentUser,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return TypedResults.Ok(new PushStatusResponse(false));
        }

        byte[] endpointHash = PushEndpointHash.Compute(endpoint);

        bool exists = await database.PushSubscriptions.AnyAsync(
            subscription => subscription.EndpointHash == endpointHash
                && subscription.UserId == currentUser.UserId,
            cancellationToken);

        return TypedResults.Ok(new PushStatusResponse(exists));
    }

    private static async Task<Ok<NotificationLogRow[]>> GetLogAsync(
        HttpContext httpContext,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        Guid leagueId = httpContext.GetMembership().LeagueId;

        var rows = await database.NotificationLog
            .AsNoTracking()
            .Where(entry => entry.LeagueId == leagueId)
            .OrderByDescending(entry => entry.CreatedUtc)
            .Take(LogRowLimit)
            .Select(entry => new
            {
                entry.CreatedUtc,
                entry.UserId,
                entry.Week,
                entry.Type,
                entry.Result,
                entry.Error,
            })
            .ToListAsync(cancellationToken);

        // Effective per-league names always come from MemberNameProjection (AGENT-NOTES.md); one
        // batched read of a roster that is capped at 50, not a join per row.
        List<Membership> members = await database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == leagueId)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> names = members.ToDictionary(
            membership => membership.UserId,
            MemberNameProjection.Effective);

        NotificationLogRow[] result =
        [
            .. rows.Select(row => new NotificationLogRow(
                new DateTimeOffset(DateTime.SpecifyKind(row.CreatedUtc, DateTimeKind.Utc)),
                names.TryGetValue(row.UserId, out string? name) ? name : "Former member",
                row.Week,
                row.Type,
                row.Result,
                row.Error)),
        ];

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<NotificationResult>, ProblemHttpResult>> SendTestAsync(
        ICurrentUser currentUser,
        AppDbContext database,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        Guid? leagueId = await database.Memberships
            .AsNoTracking()
            .Where(membership => membership.UserId == currentUser.UserId
                && membership.RemovedUtc == null
                && membership.Role == MembershipRole.Commissioner)
            .OrderBy(membership => membership.JoinedUtc)
            .Select(membership => (Guid?)membership.LeagueId)
            .FirstOrDefaultAsync(cancellationToken);

        if (leagueId is not Guid league)
        {
            return TypedResults.Problem(
                title: "You do not commission a league to send a test notification in.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // GamesAdded rather than a reminder type: the reminder types are once per week, and a
        // developer pressing the test button twice must not burn the real Friday reminder.
        NotificationResult result = await notifications.SendToUserAsync(
            currentUser.UserId,
            NotificationType.GamesAdded,
            league,
            TestPushWeek,
            new PushPayload(
                "Test notification",
                "Push is working on this device.",
                $"/leagues/{league}",
                "test"),
            ttl: null,
            cancellationToken);

        return TypedResults.Ok(result);
    }
}
