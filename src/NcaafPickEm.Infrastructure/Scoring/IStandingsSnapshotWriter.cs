namespace NcaafPickEm.Infrastructure.Scoring;

/// <summary>
/// Freezes the season standings for a week that has just become complete
/// (<c>04-Domain-Algorithms.md</c> sections 7 and 8, D-007). The week scorer calls it; the
/// leaderboard's trend arrows read what it wrote.
/// </summary>
public interface IStandingsSnapshotWriter
{
    /// <summary>
    /// Writes (or rewrites) the <c>SeasonStandingsSnapshots</c> rows for one league's
    /// <paramref name="throughWeek"/>. Idempotent: calling it twice for the same week leaves the
    /// same rows behind.
    /// </summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="throughWeek">The week that just completed, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteSnapshotAsync(Guid leagueId, int throughWeek, CancellationToken cancellationToken);
}
