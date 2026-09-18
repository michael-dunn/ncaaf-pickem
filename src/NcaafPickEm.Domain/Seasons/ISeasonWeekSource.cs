namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// Supplies the week calendar for a season. Implemented outside the Domain: a fixture source now,
/// a source over the ingested CFBD calendar from P2-02 on.
/// </summary>
public interface ISeasonWeekSource
{
    /// <summary>
    /// Returns every week of a season, ordered by <see cref="SeasonWeek.Week"/> ascending.
    /// Returns an empty list for a season the source knows nothing about.
    /// </summary>
    /// <param name="seasonYear">Calendar year of the season, e.g. 2026.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<SeasonWeek>> GetWeeksAsync(
        int seasonYear,
        CancellationToken cancellationToken = default);
}
