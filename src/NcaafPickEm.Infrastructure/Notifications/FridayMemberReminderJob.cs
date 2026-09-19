using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Catalog #1 (Feature 11, <c>04-Domain-Algorithms.md</c> section 11): Friday 8:00 PM ET reminder
/// to every member who has not submitted the current week's picks.
/// </summary>
/// <remarks>
/// Idempotent by construction: recipients are re-queried from <c>WeekSubmissions</c> at run time,
/// and <see cref="NotificationService.SendToUserAsync"/>'s once-per-week index makes a retried or
/// re-run occurrence a no-op (<see cref="NotificationResult.Skipped"/>) rather than a double send.
/// </remarks>
public sealed class FridayMemberReminderJob : IScheduledJob
{
    private readonly AppDbContext _database;
    private readonly ISeasonWeekSource _weekSource;
    private readonly NotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FridayMemberReminderJob> _logger;

    /// <summary>Creates the job.</summary>
    public FridayMemberReminderJob(
        AppDbContext database,
        ISeasonWeekSource weekSource,
        NotificationService notifications,
        TimeProvider timeProvider,
        ILogger<FridayMemberReminderJob> logger)
    {
        _database = database;
        _weekSource = weekSource;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "FridayMemberReminder";

    /// <inheritdoc />
    public string CronExpression => "0 20 * * 5";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        IReadOnlyList<ReminderRecipients.CurrentSet> targets = await ReminderRecipients.CurrentUnlockedSetsAsync(
            _database, _weekSource, nowUtc, cancellationToken);

        int sent = 0;
        int skipped = 0;
        int failed = 0;
        int leaguesFailed = 0;

        foreach (ReminderRecipients.CurrentSet target in targets)
        {
            // One league's bad data must not cost every other league its reminder: the scheduler
            // claims this occurrence before running it, so an exception escaping here would lose
            // the rest of the week's reminders for good rather than retrying them.
            try
            {
                List<ReminderRecipients.UnsubmittedMember> members = await ReminderRecipients.UnsubmittedMembersAsync(
                    _database, target.LeagueId, target.Set.Id, cancellationToken);

                foreach (ReminderRecipients.UnsubmittedMember member in members)
                {
                    int picksLeft = await ReminderRecipients.PicksLeftAsync(
                        _database, member.MembershipId, target.Set.Id, cancellationToken);

                    PushPayload payload = NotificationMessages.FridayMemberReminder(
                        target.Week, picksLeft, target.Set.LockAtUtc!.Value, target.LeagueId);

                    NotificationResult result = await _notifications.SendToUserAsync(
                        member.UserId,
                        NotificationType.FridayReminder,
                        target.LeagueId,
                        target.Week,
                        payload,
                        ttl: null,
                        cancellationToken);

                    Tally(result, ref sent, ref skipped, ref failed);
                }
            }
#pragma warning disable CA1031 // Whatever went wrong for this league, the next one still gets its reminder.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                leaguesFailed++;
                _database.ChangeTracker.Clear();
                _logger.LogError(
                    exception,
                    "{JobName}: league {LeagueId} week {Week} failed; continuing with the remaining leagues",
                    Name,
                    target.LeagueId,
                    target.Week);
            }
        }

        _logger.LogInformation(
            "{JobName} for occurrence {ScheduledFor:o} over {LeagueCount} league(s): sent {Sent}, skipped {Skipped}, failed {Failed}, leagues errored {LeaguesFailed}",
            Name,
            scheduledFor,
            targets.Count,
            sent,
            skipped,
            failed,
            leaguesFailed);
    }

    private static void Tally(NotificationResult result, ref int sent, ref int skipped, ref int failed)
    {
        switch (result)
        {
            case NotificationResult.Sent:
                sent++;
                break;
            case NotificationResult.Skipped:
                skipped++;
                break;
            default:
                failed++;
                break;
        }
    }
}
