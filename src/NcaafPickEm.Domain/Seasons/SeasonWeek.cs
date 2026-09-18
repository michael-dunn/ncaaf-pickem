namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// One provider-numbered week of a season and the window it occupies
/// (Sunday 00:00:00 ET through Saturday 23:59:59.999 ET, 04-Domain-Algorithms.md section 1).
/// </summary>
/// <param name="SeasonYear">Calendar year of the season, e.g. 2026.</param>
/// <param name="Week">Week number as the data provider numbers it. Week 0 exists in some seasons.</param>
/// <param name="StartUtc">Sunday 00:00:00 Eastern for this week, in UTC.</param>
/// <param name="EndUtc">Saturday 23:59:59.999 Eastern for this week, in UTC. Inclusive.</param>
/// <param name="IsRegularSeason">
/// False for conference championship week and anything after it. Only regular-season weeks are
/// playable (Feature 13).
/// </param>
public sealed record SeasonWeek(
    int SeasonYear,
    int Week,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsRegularSeason)
{
    /// <summary>True when <paramref name="utc"/> falls inside this week's window, ends included.</summary>
    /// <param name="utc">An instant in UTC.</param>
    public bool Contains(DateTimeOffset utc) => utc >= StartUtc && utc <= EndUtc;
}
