using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Scoring;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Keeps <c>WeekResults</c> and <c>WeekGameSets.IsComplete</c> in step with what the games
/// actually did (Feature 06, <c>04-Domain-Algorithms.md</c> section 7). The pure
/// <see cref="WeekScorer"/> decides the numbers; this loads the rows, writes the answer, and
/// decides whether a season standings snapshot has just fallen due.
/// </summary>
/// <remarks>
/// Every entry point is a full recompute of a whole week from source rows (D-006), so running it
/// twice is the same as running it once. That is what makes the three triggers safe to overlap: a
/// <c>GameWentFinal</c> per game as Saturday unfolds, a commissioner's correction, and the
/// nightly sweep that catches whatever a lost event dispatch dropped.
/// </remarks>
public sealed class ScoringService
{
    private readonly AppDbContext _database;
    private readonly IStandingsSnapshotWriter _snapshotWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScoringService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="database">The database.</param>
    /// <param name="snapshotWriter">Writes the season snapshot when a week first completes.</param>
    /// <param name="timeProvider">The clock, for <c>WeekResults.ComputedUtc</c>.</param>
    /// <param name="logger">Structured log sink.</param>
    public ScoringService(
        AppDbContext database,
        IStandingsSnapshotWriter snapshotWriter,
        TimeProvider timeProvider,
        ILogger<ScoringService> logger)
    {
        _database = database;
        _snapshotWriter = snapshotWriter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Recomputes one league week from scratch and upserts its <c>WeekResults</c>.
    /// </summary>
    /// <param name="weekGameSetId">The <c>WeekGameSets.Id</c> to score.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What the scorer produced, or <see langword="null"/> when the week was not scored at all -
    /// it does not exist, or it has not locked yet, in which case there are no results to hold.
    /// </returns>
    /// <remarks>
    /// The season standings snapshot is written after <c>SaveChangesAsync</c>, and only on the
    /// transition from not-complete to complete, so the writer reads <c>WeekResults</c> that
    /// already agree with the week that just closed and a second rescore of a closed week does not
    /// rewrite history.
    /// </remarks>
    public async Task<WeekScoreResult?> RescoreWeekAsync(
        Guid weekGameSetId,
        CancellationToken cancellationToken = default)
    {
        WeekGameSet? set = await _database.WeekGameSets
            .FirstOrDefaultAsync(candidate => candidate.Id == weekGameSetId, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            _logger.LogWarning("Week game set {WeekGameSetId} no longer exists; nothing to score", weekGameSetId);
            return null;
        }

        if (set.LockedUtc is null)
        {
            // Nothing before lock has a result: point values are not frozen, the roster is not
            // settled, and members are still changing their picks.
            _logger.LogDebug(
                "League {LeagueId} week {Week} has not locked; skipping the rescore",
                set.LeagueId,
                set.Week);
            return null;
        }

        WeekScoringRequest request = await BuildRequestAsync(set.Id, cancellationToken).ConfigureAwait(false);
        WeekScoreResult result = WeekScorer.Score(request);

        await UpsertResultsAsync(set.Id, result, cancellationToken).ConfigureAwait(false);

        bool wasComplete = set.IsComplete;
        set.IsComplete = result.IsWeekComplete;

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Scored league {LeagueId} week {Week}: {MemberCount} members over {GameCount} active games, "
            + "complete={IsComplete}, {NeedsReviewCount} games needing review",
            set.LeagueId,
            set.Week,
            result.Members.Count,
            result.Members.Count == 0 ? 0 : result.Members[0].ActiveGameCount,
            result.IsWeekComplete,
            result.NeedsReviewGameSetGameIds.Count);

        if (!wasComplete && result.IsWeekComplete)
        {
            _logger.LogInformation(
                "League {LeagueId} week {Week} is complete; writing the season standings snapshot",
                set.LeagueId,
                set.Week);

            await _snapshotWriter
                .WriteSnapshotAsync(set.LeagueId, set.Week, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Recomputes every locked week, in any league, that still carries this game.
    /// </summary>
    /// <param name="gameId">The <c>Games.Id</c> that changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many weeks were rescored.</returns>
    /// <remarks>
    /// One <c>Games</c> row is shared by every league that picked it, so a single
    /// <c>GameWentFinal</c> can close out a dozen different weeks. Rows that are removed or voided
    /// are skipped: neither contributes to any score or to completeness, so nothing about their
    /// week can have changed. Unlocked sets are skipped for the same reason
    /// <see cref="RescoreWeekAsync"/> skips them.
    /// </remarks>
    public async Task<int> RescoreGameAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        List<Guid> setIds = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.GameId == gameId
                && !row.IsRemoved
                && !row.IsVoided
                && row.WeekGameSet!.LockedUtc != null)
            .Select(row => row.WeekGameSetId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (setIds.Count == 0)
        {
            _logger.LogDebug("Game {GameId} is in no locked week game set; nothing to rescore", gameId);
            return 0;
        }

        foreach (Guid setId in setIds)
        {
            await RescoreWeekAsync(setId, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Game {GameId} triggered a rescore of {WeekCount} league weeks", gameId, setIds.Count);
        return setIds.Count;
    }

    private async Task<WeekScoringRequest> BuildRequestAsync(Guid weekGameSetId, CancellationToken cancellationToken)
    {
        List<ScoringGame> games = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == weekGameSetId)
            .OrderBy(row => row.Game!.KickoffUtc)
            .ThenBy(row => row.Id)
            .Select(row => new ScoringGame(
                row.Id,
                row.Game!.HomeTeamId,
                row.Game.AwayTeamId,
                row.ResolvedPointValue,
                row.Game.Status,
                row.Game.HomeScore,
                row.Game.AwayScore,
                row.ResultOverrideWinnerTeamId,
                row.IsVoided,
                row.IsRemoved))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Every membership the lock job settled, and only those: the WeekSubmissions row is the
        // whole of "active at lock" (D-111), including a member removed since and excluding one
        // who joined after. WeekScorer drops any row the job did not settle.
        List<ScoringMember> members = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == weekGameSetId)
            .OrderBy(submission => submission.MembershipId)
            .Select(submission => new ScoringMember(submission.MembershipId, submission.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ScoringPick> picks = await _database.Picks
            .AsNoTracking()
            .Where(pick => pick.WeekGameSetGame!.WeekGameSetId == weekGameSetId)
            .Select(pick => new ScoringPick(pick.MembershipId, pick.WeekGameSetGameId, pick.PickedTeamId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new WeekScoringRequest
        {
            Games = games,
            Members = members,
            Picks = picks,
        };
    }

    /// <summary>
    /// Writes one <c>WeekResults</c> row per scored membership, creating the ones that do not
    /// exist yet and removing any that the scorer no longer produces.
    /// </summary>
    private async Task UpsertResultsAsync(
        Guid weekGameSetId,
        WeekScoreResult result,
        CancellationToken cancellationToken)
    {
        List<WeekResult> existing = await _database.WeekResults
            .Where(row => row.WeekGameSetId == weekGameSetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, WeekResult> byMembership = existing.ToDictionary(row => row.MembershipId);
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (MemberWeekScore score in result.Members)
        {
            if (!byMembership.Remove(score.MembershipId, out WeekResult? row))
            {
                row = new WeekResult
                {
                    WeekGameSetId = weekGameSetId,
                    MembershipId = score.MembershipId,
                };
                _database.WeekResults.Add(row);
            }

            row.Points = score.Points;
            row.CorrectCount = score.CorrectCount;
            row.ActiveGameCount = score.ActiveGameCount;
            row.IsWeekComplete = result.IsWeekComplete;

            // Stamped on every recompute, including one that changes no number: the column says
            // when this row was last computed, and the nightly sweep exists precisely to be able
            // to say "yes, still right" about a week nobody has touched (D-127).
            row.ComputedUtc = nowUtc;
        }

        // Anything left belongs to a membership the scorer no longer produces - its
        // WeekSubmissions row was removed or rolled back to a pre-lock status. A full recompute is
        // authoritative, so the stale result goes rather than lingering on the leaderboard.
        foreach (WeekResult orphan in byMembership.Values)
        {
            _logger.LogWarning(
                "Removing the week result for membership {MembershipId} in set {WeekGameSetId}: "
                + "it no longer has a settled submission row",
                orphan.MembershipId,
                weekGameSetId);

            _database.WeekResults.Remove(orphan);
        }
    }
}
