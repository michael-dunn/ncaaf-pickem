namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// The span of weeks a league plays, inclusive. Weeks outside it have no game set and are not
/// pickable or navigable (Feature 13).
/// </summary>
/// <param name="FirstWeek">First week the league plays. Defaults to 1, never 0.</param>
/// <param name="LastWeek">Last week the league plays. Defaults to the final regular-season week.</param>
public readonly record struct LeagueWeekRange(int FirstWeek, int LastWeek)
{
    /// <summary>True when <paramref name="week"/> is inside the range.</summary>
    /// <param name="week">A week number.</param>
    public bool Contains(int week) => week >= FirstWeek && week <= LastWeek;
}
