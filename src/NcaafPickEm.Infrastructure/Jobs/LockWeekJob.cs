using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Events;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Points;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// Locks a league's week the moment its first game kicks off (Features 04 and 05,
/// <c>04-Domain-Algorithms.md</c> section 5): snapshots each active game's spread and point
/// value, settles every member's status to Locked or Incomplete, stamps <c>LockedUtc</c>, and
/// raises <see cref="WeekLocked"/>.
/// </summary>
/// <remarks>
/// <para>
/// A one-shot (D-034) rather than a cron job, because the due time is per week and per league and
/// lives in the database. The card's "swept every minute from Friday 18:00 to Sunday 03:00
/// Eastern" falls out of that: <see cref="SchedulerTick"/> runs every minute all week, and
/// <see cref="GetDueAsync"/> answers "every week whose <c>LockAtUtc</c> has passed and which is
/// not locked yet" — so a set whose lock instant went by hours ago, while the process was down,
/// is still offered on the first tick after it comes back. There is no window to fall outside of
/// and nothing to catch up on by hand.
/// </para>
/// <para>
/// The <c>JobRuns</c> key is <c>LockWeek:{weekGameSetId:N}</c> (41 characters, inside the 60 the
/// scheduler allows) and <c>ScheduledForUtc</c> is the set's <c>LockAtUtc</c>, so a lock instant
/// that moves (a game added or removed before kickoff) is a genuinely new occurrence rather than
/// a duplicate of the old one. <see cref="RunAsync"/> is idempotent regardless: it reloads the
/// set and returns immediately if something already locked it (D-033).
/// </para>
/// </remarks>
public sealed class LockWeekJob : IOneShotJob
{
    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly DomainEventCollector _collector;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly ILogger<LockWeekJob> _logger;

    /// <summary>Creates the job.</summary>
    /// <param name="database">The context.</param>
    /// <param name="timeProvider">The clock; its reading becomes <c>LockedUtc</c>.</param>
    /// <param name="collector">Collects <see cref="WeekLocked"/> for dispatch after the save.</param>
    /// <param name="dispatcher">Delivers it.</param>
    /// <param name="logger">Log sink.</param>
    public LockWeekJob(
        AppDbContext database,
        TimeProvider timeProvider,
        DomainEventCollector collector,
        IDomainEventDispatcher dispatcher,
        ILogger<LockWeekJob> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _collector = collector;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "LockWeek";

    /// <inheritdoc />
    public async Task<IReadOnlyList<OneShotOccurrence>> GetDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        DateTime cutoff = nowUtc.UtcDateTime;

        var due = await _database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.LockAtUtc != null && set.LockAtUtc <= cutoff && set.LockedUtc == null)
            .OrderBy(set => set.LockAtUtc)
            .Select(set => new { set.Id, set.LockAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. due.Select(row => new OneShotOccurrence(
            row.Id.ToString("N"),
            new DateTimeOffset(DateTime.SpecifyKind(row.LockAtUtc!.Value, DateTimeKind.Utc))))];
    }

    /// <inheritdoc />
    public async Task RunAsync(OneShotOccurrence occurrence, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(occurrence.Key, "N", out Guid weekGameSetId))
        {
            _logger.LogWarning("Ignoring a week-lock occurrence with an unreadable key {Key}", occurrence.Key);
            return;
        }

        WeekGameSet? set = await _database.WeekGameSets
            .FirstOrDefaultAsync(candidate => candidate.Id == weekGameSetId, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            _logger.LogWarning("Week game set {WeekGameSetId} no longer exists; nothing to lock", weekGameSetId);
            return;
        }

        if (set.LockedUtc is not null)
        {
            // Another tick, or this process before it restarted, already locked the week. Section
            // 5's idempotency clause: find LockedUtc set and skip, touching nothing.
            _logger.LogDebug(
                "League {LeagueId} week {Week} was already locked at {LockedUtc:o}; skipping",
                set.LeagueId,
                set.Week,
                set.LockedUtc);
            return;
        }

        League league = await _database.Leagues
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == set.LeagueId, cancellationToken)
            .ConfigureAwait(false);

        List<WeekGameSetGame> rows = await _database.WeekGameSetGames
            .Include(row => row.Game!.HomeTeam)
            .Include(row => row.Game!.AwayTeam)
            .Where(row => row.WeekGameSetId == set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        WeekLockRequest request = await BuildRequestAsync(set, league, rows, cancellationToken).ConfigureAwait(false);
        WeekLockResult result = WeekLocker.Lock(request);

        Dictionary<Guid, WeekGameSetGame> rowsById = rows.ToDictionary(row => row.Id);
        foreach (LockedGameSnapshot snapshot in result.Games)
        {
            WeekGameSetGame row = rowsById[snapshot.GameSetGameId];
            row.SpreadAtLock = snapshot.SpreadAtLock;
            row.ResolvedPointValue = snapshot.FrozenPointValue;
        }

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await ApplyStatusesAsync(set.Id, result.Members, nowUtc, cancellationToken).ConfigureAwait(false);

        // Step 3: the instant the job ran, not LockAtUtc, so a late run is visible in the data.
        set.LockedUtc = nowUtc;

        _collector.Raise(new WeekLocked(set.LeagueId, set.Week, set.Id, nowUtc) { OccurredUtc = nowUtc });

        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "League {LeagueId} week {Week} locked at {LockedUtc:o} (due {LockAtUtc:o}): "
            + "{GameCount} games frozen, {MemberCount} member statuses written.",
            set.LeagueId,
            set.Week,
            nowUtc,
            occurrence.DueUtc,
            result.Games.Count,
            result.Members.Count);
    }

    private async Task<WeekLockRequest> BuildRequestAsync(
        WeekGameSet set,
        League league,
        IReadOnlyList<WeekGameSetGame> rows,
        CancellationToken cancellationToken)
    {
        Guid[] gameIds = [.. rows.Where(row => row.IsActive).Select(row => row.GameId).Distinct()];

        Dictionary<Guid, decimal> spreads = await PointValueRecalculator
            .LoadLatestSpreadsAsync(_database, gameIds, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PointRuleInfo> pointRules = await PointValueRecalculator
            .LoadPointRuleInfosAsync(_database, set.LeagueId, cancellationToken)
            .ConfigureAwait(false);

        WeekLockGame[] games = [.. rows.Select(row => new WeekLockGame(
            row.Id,
            row.AddedUtc,
            row.IsActive,
            new PointGameInfo(
                row.Game!.HomeTeamId,
                row.Game!.AwayTeamId,
                row.Game!.HomeTeam?.ConferenceId,
                row.Game!.AwayTeam?.ConferenceId,
                row.Game!.IsConferenceGame),
            row.PointValueOverride,
            spreads.TryGetValue(row.GameId, out decimal spread) ? spread : null))];

        var memberships = await _database.Memberships
            .AsNoTracking()
            .Where(membership => membership.LeagueId == set.LeagueId)
            .Select(membership => new { membership.Id, membership.JoinedUtc, membership.RemovedUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] membershipIds = [.. memberships.Select(membership => membership.Id)];

        var picks = await _database.Picks
            .AsNoTracking()
            .Where(pick => membershipIds.Contains(pick.MembershipId)
                && pick.WeekGameSetGame!.WeekGameSetId == set.Id)
            .Select(pick => new { pick.MembershipId, pick.WeekGameSetGameId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<Guid>> picksByMembership = picks
            .GroupBy(pick => pick.MembershipId)
            .ToDictionary(group => group.Key, group => group.Select(pick => pick.WeekGameSetGameId).ToList());

        Dictionary<Guid, DateTime?> submittedByMembership = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == set.Id)
            .ToDictionaryAsync(
                submission => submission.MembershipId,
                submission => submission.SubmittedUtc,
                cancellationToken)
            .ConfigureAwait(false);

        WeekLockMember[] members = [.. memberships.Select(membership => new WeekLockMember(
            membership.Id,
            membership.JoinedUtc,
            membership.RemovedUtc is null,
            picksByMembership.TryGetValue(membership.Id, out List<Guid>? picked) ? picked : [],
            submittedByMembership.TryGetValue(membership.Id, out DateTime? submittedUtc) ? submittedUtc : null))];

        return new WeekLockRequest
        {
            Games = games,
            Members = members,
            PointRules = pointRules,
            LeagueDefaultPointValue = league.DefaultPointValue,

            // LockAtUtc, not the clock: a run that is an hour late must still decide "joined
            // before picks froze" against the moment picks actually froze.
            LockInstantUtc = set.LockAtUtc ?? _timeProvider.GetUtcNow().UtcDateTime,
        };
    }

    /// <summary>
    /// Writes one <c>WeekSubmissions</c> row per membership the locker kept, creating it when the
    /// member never touched the week. Rows belonging to memberships the locker dropped (removed
    /// before lock, or joined after it) are left exactly as they are rather than deleted.
    /// </summary>
    private async Task ApplyStatusesAsync(
        Guid weekGameSetId,
        IReadOnlyList<LockedMemberStatus> statuses,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (statuses.Count == 0)
        {
            return;
        }

        List<WeekSubmission> existing = await _database.WeekSubmissions
            .Where(submission => submission.WeekGameSetId == weekGameSetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, WeekSubmission> byMembership = existing.ToDictionary(row => row.MembershipId);

        foreach (LockedMemberStatus status in statuses)
        {
            if (!byMembership.TryGetValue(status.MembershipId, out WeekSubmission? submission))
            {
                submission = new WeekSubmission
                {
                    WeekGameSetId = weekGameSetId,
                    MembershipId = status.MembershipId,
                    HasUnseenGameChanges = false,
                };
                _database.WeekSubmissions.Add(submission);
            }

            submission.Status = status.Status;
            submission.LastChangedUtc = nowUtc;
        }
    }
}
