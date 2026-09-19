using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Leaderboard;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Infrastructure.Scoring;

/// <summary>
/// The real <see cref="IStandingsSnapshotWriter"/> (P5-03): recomputes the season standings through
/// one week with <see cref="StandingsCalculator.ComputeSnapshot"/> and upserts them into
/// <c>SeasonStandingsSnapshots</c>.
/// </summary>
/// <remarks>
/// Idempotent by construction: it recomputes every row for the <c>ThroughWeek</c> from the current
/// <c>WeekResults</c> and deletes any stale row for that week, so a rescore that fires the same
/// week twice - or a nightly recompute after a correction - converges rather than accumulating.
/// Only active memberships get a row, matching the season leaderboard the arrows compare.
/// </remarks>
public sealed class StandingsSnapshotWriter : IStandingsSnapshotWriter
{
    private readonly AppDbContext _database;
    private readonly ILogger<StandingsSnapshotWriter> _logger;

    /// <summary>Creates the writer.</summary>
    /// <param name="database">The database.</param>
    /// <param name="logger">Logger.</param>
    public StandingsSnapshotWriter(AppDbContext database, ILogger<StandingsSnapshotWriter> logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteSnapshotAsync(Guid leagueId, int throughWeek, CancellationToken cancellationToken)
    {
        // Names only break ties between equal totals, so the effective-name rule still has to be
        // the shared one (MemberNameProjection) or two members on the same score could rank
        // differently here than on the leaderboard the arrows compare against.
        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == leagueId && membership.RemovedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        StandingsMember[] members =
        [
            .. memberships.Select(membership => new StandingsMember(
                membership.Id,
                MemberNameProjection.Effective(membership),
                IsFormer: false,
                membership.JoinedWeek))
        ];

        StandingsWeekResult[] results = await _database.WeekResults
            .AsNoTracking()
            .Where(result => result.WeekGameSet!.LeagueId == leagueId && result.WeekGameSet!.Week <= throughWeek)
            .Select(result => new StandingsWeekResult(
                result.MembershipId,
                result.WeekGameSet!.Week,
                result.Points,
                result.CorrectCount,
                result.ActiveGameCount,
                result.IsWeekComplete))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        StandingsSnapshotRow[] rows = StandingsCalculator.ComputeSnapshot(members, results, throughWeek);

        List<SeasonStandingsSnapshot> existing = await _database.SeasonStandingsSnapshots
            .Where(snapshot => snapshot.LeagueId == leagueId && snapshot.ThroughWeek == throughWeek)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, SeasonStandingsSnapshot> existingByMembership = existing
            .ToDictionary(snapshot => snapshot.MembershipId);

        foreach (StandingsSnapshotRow row in rows)
        {
            if (existingByMembership.Remove(row.MembershipId, out SeasonStandingsSnapshot? snapshot))
            {
                snapshot.Rank = row.Rank;
                snapshot.TotalPoints = row.TotalPoints;
                continue;
            }

            _database.SeasonStandingsSnapshots.Add(new SeasonStandingsSnapshot
            {
                LeagueId = leagueId,
                ThroughWeek = throughWeek,
                MembershipId = row.MembershipId,
                Rank = row.Rank,
                TotalPoints = row.TotalPoints,
            });
        }

        // Whatever is left belongs to a membership that is no longer active; the arrows read the
        // active standings, so the row goes.
        _database.SeasonStandingsSnapshots.RemoveRange(existingByMembership.Values);

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "League {LeagueId}: standings snapshot written through week {ThroughWeek} ({RowCount} rows).",
            leagueId,
            throughWeek,
            rows.Length);
    }
}
