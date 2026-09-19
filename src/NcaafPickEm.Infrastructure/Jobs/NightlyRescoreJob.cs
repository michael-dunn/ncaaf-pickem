using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// Recomputes every week that could still be wrong, at 04:30 Eastern (Feature 06,
/// <c>04-Domain-Algorithms.md</c> section 7's "nightly full recompute for safety").
/// </summary>
/// <remarks>
/// <para>
/// The scoring event handlers are the fast path, and a handler that throws is logged and skipped
/// rather than retried (D-046) - so something has to be the backstop, and this is it. A crash
/// between <c>SaveChangesAsync</c> and the dispatch, a handler that failed on a deadlock, a game
/// whose Final arrived while the process was restarting: all of them look the same by the small
/// hours, and all of them are fixed by recomputing the week from its rows.
/// </para>
/// <para>
/// Two kinds of week qualify. Locked weeks that are not complete are the obvious ones - they are
/// still waiting on something, so their numbers can still move. The second kind is the safety net
/// proper: a week whose set says complete while one of its own <c>WeekResults</c> rows says
/// otherwise, which is what a rescore interrupted between writing the rows and stamping the set
/// leaves behind.
/// </para>
/// <para>
/// 04:30 Eastern is after the latest Saturday finish (a west-coast night game ending near 02:00
/// ET) and before the daily schedule refresh at 04:00... which it deliberately follows, so a
/// postponement ingested overnight is already visible in <c>Games.Status</c> by the time the week
/// is scored again.
/// </para>
/// </remarks>
public sealed class NightlyRescoreJob : IScheduledJob
{
    private readonly AppDbContext _database;
    private readonly ScoringService _scoring;
    private readonly ILogger<NightlyRescoreJob> _logger;

    /// <summary>Creates the job.</summary>
    /// <param name="database">The database, for the "which weeks are suspect" query.</param>
    /// <param name="scoring">The scorer that does the work.</param>
    /// <param name="logger">Structured log sink.</param>
    public NightlyRescoreJob(AppDbContext database, ScoringService scoring, ILogger<NightlyRescoreJob> logger)
    {
        _database = database;
        _scoring = scoring;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "NightlyRescore";

    /// <inheritdoc />
    public string CronExpression => "30 4 * * *";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        List<Guid> setIds = await _database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.LockedUtc != null
                && (!set.IsComplete
                    || _database.WeekResults.Any(result =>
                        result.WeekGameSetId == set.Id && !result.IsWeekComplete)))
            .OrderBy(set => set.Week)
            .ThenBy(set => set.Id)
            .Select(set => set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (setIds.Count == 0)
        {
            _logger.LogInformation(
                "Nightly rescore for occurrence {ScheduledFor:o}: no locked week needs recomputing",
                scheduledFor);
            return;
        }

        int rescored = 0;
        int failed = 0;

        foreach (Guid setId in setIds)
        {
            try
            {
                await _scoring.RescoreWeekAsync(setId, cancellationToken).ConfigureAwait(false);
                rescored++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One bad week must not stop the sweep - the whole point of the job is that it
                // gets to every week that might be wrong (D-115's per-league isolation, applied
                // per week here).
                failed++;
                _logger.LogError(exception, "Nightly rescore of week game set {WeekGameSetId} failed", setId);
            }
            finally
            {
                // Each week is its own unit of work; nothing from the previous one should still be
                // tracked, least of all a half-applied change left behind by a failure.
                _database.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "Nightly rescore for occurrence {ScheduledFor:o}: {Rescored} of {Candidate} week game sets "
            + "recomputed, {Failed} failed",
            scheduledFor,
            rescored,
            setIds.Count,
            failed);
    }
}
