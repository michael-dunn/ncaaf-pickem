using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Catalog #2 (Feature 11, <c>04-Domain-Algorithms.md</c> section 11): Friday 9:00 PM ET roll-up
/// to every active commissioner naming who has not submitted, sent only when at least one member
/// has not.
/// </summary>
public sealed class FridayCommissionerSummaryJob : IScheduledJob
{
    private readonly AppDbContext _database;
    private readonly ISeasonWeekSource _weekSource;
    private readonly NotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FridayCommissionerSummaryJob> _logger;

    /// <summary>Creates the job.</summary>
    public FridayCommissionerSummaryJob(
        AppDbContext database,
        ISeasonWeekSource weekSource,
        NotificationService notifications,
        TimeProvider timeProvider,
        ILogger<FridayCommissionerSummaryJob> logger)
    {
        _database = database;
        _weekSource = weekSource;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "FridayCommissionerSummary";

    /// <inheritdoc />
    public string CronExpression => "0 21 * * 5";

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
            // One league's bad data must not cost every other league its summary: the scheduler
            // claims this occurrence before running it, so an exception escaping here would lose
            // the rest of the week's summaries for good rather than retrying them.
            try
            {
                List<ReminderRecipients.UnsubmittedMember> unsubmitted = await ReminderRecipients.UnsubmittedMembersAsync(
                    _database, target.LeagueId, target.Set.Id, cancellationToken);

                if (unsubmitted.Count == 0)
                {
                    // Card: only when at least one member is unsubmitted.
                    continue;
                }

                List<Guid> commissionerUserIds = await _database.Memberships
                    .AsNoTracking()
                    .Where(m => m.LeagueId == target.LeagueId
                        && m.RemovedUtc == null
                        && m.Role == MembershipRole.Commissioner)
                    .Select(m => m.UserId)
                    .ToListAsync(cancellationToken);

                string names = string.Join(", ", unsubmitted.Select(member => member.Name));
                PushPayload payload = NotificationMessages.CommissionerSummary(
                    target.Week, unsubmitted.Count, names, target.LeagueId);

                foreach (Guid commissionerUserId in commissionerUserIds)
                {
                    NotificationResult result = await _notifications.SendToUserAsync(
                        commissionerUserId,
                        NotificationType.CommissionerSummary,
                        target.LeagueId,
                        target.Week,
                        payload,
                        ttl: null,
                        cancellationToken);

                    Tally(result, ref sent, ref skipped, ref failed);
                }
            }
#pragma warning disable CA1031 // Whatever went wrong for this league, the next one still gets its summary.
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
