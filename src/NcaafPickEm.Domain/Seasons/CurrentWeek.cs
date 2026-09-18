namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// The week a season is on right now, and why.
/// </summary>
/// <param name="Week">
/// The week whose window contains now; the first week before the season starts; the last
/// regular-season week once the season is over.
/// </param>
/// <param name="State">Whether the season has started, is running, or is finished.</param>
public readonly record struct CurrentWeek(int Week, SeasonState State)
{
    /// <summary>True once the last regular-season week's window has closed.</summary>
    public bool IsSeasonOver => State == SeasonState.SeasonOver;
}
