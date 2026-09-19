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
/// not assume it runs before or after that one. It treats a membership as "was Submitted" when
/// its current status is still <see cref="SubmissionStatus.Submitted"/> (P4-04 has not run, or is
/// not registered yet) <em>or</em> it has a <c>SubmittedUtc</c> earlier than this event's
/// <see cref="GameAddedToSet.OccurredUtc"/> (P4-04 already flipped it to InProgress). Either way
/// the member was Submitted at the moment the games were added, which is the card's rule.
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

        var recipients = await _database.Memberships
            .AsNoTracking()
            .Where(m => m.LeagueId == domainEvent.LeagueId && m.RemovedUtc == null)
            .Select(m => new
            {
                m.UserId,
                Submission = _database.WeekSubmissions
                    .Where(ws => ws.MembershipId == m.Id && ws.WeekGameSetId == domainEvent.WeekGameSetId)
                    .Select(ws => new { ws.Status, ws.SubmittedUtc })
                    .FirstOrDefault(),
            })
            .Where(row => row.Submission != null
                && (row.Submission.Status == SubmissionStatus.Submitted
                    || (row.Submission.SubmittedUtc != null && row.Submission.SubmittedUtc < domainEvent.OccurredUtc)))
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
