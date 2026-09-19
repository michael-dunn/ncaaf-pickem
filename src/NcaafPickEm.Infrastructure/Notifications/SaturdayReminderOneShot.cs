using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Catalog #3 (Feature 11, <c>04-Domain-Algorithms.md</c> section 11): the last-call reminder,
/// exactly one hour before a week's lock.
/// </summary>
/// <remarks>
/// A one-shot rather than a cron job because the due time — <c>LockAtUtc - 1h</c> — lives per
/// week in <c>WeekGameSets</c> and moves whenever the commissioner edits the set (D-034).
/// <see cref="GetDueAsync"/> re-reads <c>WeekGameSets</c> every tick and reports the occurrence
/// key as the set's id with the due time as its current <c>LockAtUtc - 1h</c>; the scheduler
/// dedupes on <c>(Name:Key, ScheduledForUtc)</c>, so a moved lock time produces a genuinely new
/// occurrence that runs again instead of being treated as already done.
/// </remarks>
public sealed class SaturdayReminderOneShot : IOneShotJob
{
    private static readonly TimeSpan LeadTime = TimeSpan.FromHours(1);

    private readonly AppDbContext _database;
    private readonly NotificationService _notifications;
    private readonly ILogger<SaturdayReminderOneShot> _logger;

    /// <summary>Creates the job.</summary>
    public SaturdayReminderOneShot(
        AppDbContext database,
        NotificationService notifications,
        ILogger<SaturdayReminderOneShot> logger)
    {
        _database = database;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SaturdayReminder";

    /// <inheritdoc />
    public async Task<IReadOnlyList<OneShotOccurrence>> GetDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var unlockedSets = await _database.WeekGameSets
            .AsNoTracking()
            .Where(s => s.LockedUtc == null && s.LockAtUtc != null)
            .Select(s => new { s.Id, LockAtUtc = s.LockAtUtc!.Value })
            .ToListAsync(cancellationToken);

        return unlockedSets
            .Select(row => new OneShotOccurrence(
                row.Id.ToString("N"),
                new DateTimeOffset(DateTime.SpecifyKind(row.LockAtUtc, DateTimeKind.Utc)) - LeadTime))
            .ToList();
    }

    /// <inheritdoc />
    public async Task RunAsync(OneShotOccurrence occurrence, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(occurrence.Key, "N", out Guid weekGameSetId))
        {
            _logger.LogWarning("Ignoring a Saturday reminder occurrence with an unreadable key {Key}", occurrence.Key);
            return;
        }

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == weekGameSetId, cancellationToken);

        if (set is null || set.LockedUtc is not null || set.LockAtUtc is null)
        {
            // Locked between the tick offering this occurrence and the run, or the set is gone.
            return;
        }

        List<ReminderRecipients.UnsubmittedMember> members = await ReminderRecipients.UnsubmittedMembersAsync(
            _database, set.LeagueId, set.Id, cancellationToken);

        int sent = 0;
        int skipped = 0;
        int failed = 0;

        foreach (ReminderRecipients.UnsubmittedMember member in members)
        {
            int picksLeft = await ReminderRecipients.PicksLeftAsync(
                _database, member.MembershipId, set.Id, cancellationToken);

            PushPayload payload = NotificationMessages.SaturdayReminder(set.Week, picksLeft, set.LeagueId);

            NotificationResult result = await _notifications.SendToUserAsync(
                member.UserId,
                NotificationType.SaturdayReminder,
                set.LeagueId,
                set.Week,
                payload,
                ttl: null,
                cancellationToken);

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

        _logger.LogInformation(
            "{JobName} for set {WeekGameSetId} (league {LeagueId}, week {Week}): sent {Sent}, skipped {Skipped}, failed {Failed}",
            Name,
            set.Id,
            set.LeagueId,
            set.Week,
            sent,
            skipped,
            failed);
    }
}
