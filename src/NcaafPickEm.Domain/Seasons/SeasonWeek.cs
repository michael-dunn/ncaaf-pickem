namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// One week of one season, from the provider calendar (Feature 13). The window runs Sunday
/// 00:00 Eastern to Saturday 23:59:59 Eastern, stored in UTC.
/// </summary>
public sealed class SeasonWeek
{
    public int SeasonYear { get; set; }

    /// <summary>Provider week number. Week 0 exists and is excluded from league defaults.</summary>
    public int Week { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>False from championship week onward; those weeks are out of scope.</summary>
    public bool IsRegularSeason { get; set; }
}
