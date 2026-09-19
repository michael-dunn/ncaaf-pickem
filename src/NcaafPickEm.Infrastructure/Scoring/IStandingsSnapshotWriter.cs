namespace NcaafPickEm.Infrastructure.Scoring;

/// <summary>
/// Writes the <c>SeasonStandingsSnapshots</c> rows for one league through one week (D-007). The
/// seam between P5-01's scorer, which knows <em>when</em> a snapshot is due, and P5-03's
/// standings calculator, which knows <em>what</em> is in one.
/// </summary>
/// <remarks>
/// <para>
/// <c>ScoringService</c> calls this exactly once per week, on the transition from not-complete to
/// complete, after its own <c>SaveChangesAsync</c> - so an implementation reads a database whose
/// <c>WeekResults</c> already agree with the week that just closed.
/// </para>
/// <para>
/// <c>AddInfrastructure</c> registers <see cref="NoOpStandingsSnapshotWriter"/> with
/// <c>TryAddScoped</c>, so the real implementation wins by being registered on an earlier line.
/// Implementations must be idempotent: a rescore that closes an already-closed week (a correction
/// applied, then reverted) can call this again for the same <c>ThroughWeek</c>, and the nightly
/// sweep re-runs whatever the event pipeline dropped.
/// </para>
/// </remarks>
public interface IStandingsSnapshotWriter
{
    /// <summary>Writes (or rewrites) the snapshot for one league through one week.</summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="throughWeek">The week that just became complete, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteSnapshotAsync(Guid leagueId, int throughWeek, CancellationToken cancellationToken);
}
