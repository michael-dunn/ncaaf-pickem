using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Catalog #4 (Feature 11): tells a member new games were added to a week they had already
/// submitted. Reacts to <see cref="GameAddedToSet"/>, which already carries every
/// <c>WeekGameSetGames.Id</c> added in one regeneration or manual add (P3-03), so this handler
/// sends exactly one "N new games" message per affected member rather than one per game.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who counts as "was Submitted".</b> P4-04 is expected to react to the same event by moving a
/// Submitted member to InProgress and setting <c>HasUnseenGameChanges</c>; domain event handlers
/// for one event run in registration order (<c>05-Conventions.md</c>/D-046), so this handler must
/// not assume it runs before or after that one. It therefore never reads
/// <c>WeekSubmissions.Status</c> at all: a membership counts as "was Submitted" when its
/// <c>SubmittedUtc</c> is at or after the <c>AddedUtc</c> of the newest game that was <em>already</em>
/// in the set, which is <c>SubmissionStatusCalculator</c>'s own definition of Submitted evaluated
/// against the set as it stood a moment ago (D-155).
/// </para>
/// <para>
/// That threshold, rather than the event's <see cref="GameAddedToSet.OccurredUtc"/>, is also what
/// stops the message repeating: <c>SubmittedUtc</c> is never cleared, so "submitted at some point
/// before now" stays true forever and a member who ignored the first add would have been notified
/// again by every later one. Re-submitting after an add does refresh <c>SubmittedUtc</c>
/// (<c>PickService.SubmitAsync</c> only leaves it alone when the status is already Submitted), so
/// a member who does catch up is correctly notified again next time.
/// </para>
/// </remarks>
public sealed class GamesAddedNotificationHandler : IDomainEventHandler<GameAddedToSet>
{
    private readonly AppDbContext _database;
    private readonly NotificationService _notifications;
    private readonly ILogger<GamesAddedNotificationHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public GamesAddedNotificationHandler(
        AppDbContext database,
        NotificationService notifications,
        ILogger<GamesAddedNotificationHandler> logger)
    {
        _database = database;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameAddedToSet domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.GameSetGameIds.Count == 0)
        {
            return;
        }

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == domainEvent.WeekGameSetId, cancellationToken);

        if (set is null || set.LockedUtc is not null)
        {
            // Never happens before lock in practice (generation is a no-op after lock), but a
            // handler must not assume the invariant it is reacting to still holds by the time it
            // runs.
            return;
        }

        // "Submitted immediately before this add" = submitted after the newest game that was
        // already in the set. Nothing here reads Status, so it does not matter whether P4-04's
        // handler on the same event has already flipped Submitted to InProgress, and - unlike an
        // OccurredUtc comparison - a member who never re-submitted after the first add is not
        // notified all over again by the second one (P8-01, D-155).
        DateTime? priorNewestAddedUtc = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == domainEvent.WeekGameSetId
                && !row.IsRemoved
                && !domainEvent.GameSetGameIds.Contains(row.Id))
            .MaxAsync(row => (DateTime?)row.AddedUtc, cancellationToken);

        DateTime submittedAtOrAfter = priorNewestAddedUtc ?? DateTime.MinValue;

        List<Guid> recipients = await _database.Memberships
            .AsNoTracking()
            .Where(m => m.LeagueId == domainEvent.LeagueId && m.RemovedUtc == null)
            .Select(m => new
            {
                m.UserId,
                SubmittedUtc = _database.WeekSubmissions
                    .Where(ws => ws.MembershipId == m.Id && ws.WeekGameSetId == domainEvent.WeekGameSetId)
                    .Select(ws => ws.SubmittedUtc)
                    .FirstOrDefault(),
            })
            .Where(row => row.SubmittedUtc != null && row.SubmittedUtc >= submittedAtOrAfter)
            .Select(row => row.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            return;
        }

        PushPayload payload = NotificationMessages.GamesAdded(
            domainEvent.Week, domainEvent.GameSetGameIds.Count, domainEvent.LeagueId, domainEvent.WeekGameSetId);

        int sent = 0;

        foreach (Guid userId in recipients)
        {
            NotificationResult result = await _notifications.SendToUserAsync(
                userId,
                NotificationType.GamesAdded,
                domainEvent.LeagueId,
                domainEvent.Week,
                payload,
                ttl: null,
                cancellationToken);

            if (result == NotificationResult.Sent)
            {
                sent++;
            }
        }

        _logger.LogInformation(
            "GamesAdded ({AddedCount} games) for league {LeagueId} week {Week}: notified {Recipients} previously-submitted member(s), {Sent} sent",
            domainEvent.GameSetGameIds.Count,
            domainEvent.LeagueId,
            domainEvent.Week,
            recipients.Count,
            sent);
    }
}
