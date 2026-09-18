using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Auth;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// The signed-in user's own account (Feature 08 Profile).
/// </summary>
public static class MeEndpoints
{
    /// <summary>Maps <c>GET /api/me</c> and <c>PUT /api/me</c>.</summary>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        RouteGroupBuilder me = builder.MapGroup("/me")
            .WithTags("me")
            .RequireAuthorization(PolicyNames.Authenticated);

        me.MapGet("", GetAsync).WithName("MeGet");
        me.MapPut("", UpdateAsync).WithName("MeUpdate");

        return builder;
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> GetAsync(
        ICurrentUser currentUser,
        AppDbContext database,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        User? user = await LoadAsync(currentUser, database, cancellationToken);

        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await ToResponseAsync(user, leagueService, cancellationToken));
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult, ValidationProblem>> UpdateAsync(
        UpdateMeRequest request,
        ICurrentUser currentUser,
        AppDbContext database,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        string displayName = request.DisplayName?.Trim() ?? string.Empty;

        if (displayName.Length is 0 or > User.DisplayNameMaxLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateMeRequest.DisplayName)] =
                    [$"Display name must be 1 to {User.DisplayNameMaxLength} characters."],
            });
        }

        User? user = await LoadAsync(currentUser, database, cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        user.DisplayName = displayName;
        await database.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ToResponseAsync(user, leagueService, cancellationToken));
    }

    /// <remarks>
    /// A cookie can outlive the row it names (a database restore, a hand-deleted user), so the
    /// lookup is allowed to miss and answers 401 rather than throwing.
    /// </remarks>
    private static async Task<User?> LoadAsync(
        ICurrentUser currentUser,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
        {
            return null;
        }

        return await database.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    private static async Task<MeResponse> ToResponseAsync(
        User user,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        LeagueSummary[] leagues = await leagueService.ListMineAsync(user.Id, cancellationToken);
        return new MeResponse(user.Id, user.Email, user.DisplayName, leagues);
    }
}
