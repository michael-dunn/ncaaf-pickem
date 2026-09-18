using NcaafPickEm.Domain.Users;

namespace NcaafPickEm.Domain.Leagues;

/// <summary>
/// One pick-em league for one season (Feature 01). A league never spans seasons.
/// </summary>
public sealed class League
{
    /// <summary>Maximum length of <see cref="Name"/>, in characters.</summary>
    public const int NameMaxLength = 50;

    /// <summary>Lowest legal <see cref="DefaultPointValue"/>.</summary>
    public const int MinPointValue = 1;

    /// <summary>Highest legal <see cref="DefaultPointValue"/>.</summary>
    public const int MaxPointValue = 100;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SeasonYear { get; set; }

    /// <summary>First week the league plays. Default 1 (Week 0 is excluded).</summary>
    public int FirstWeek { get; set; }

    /// <summary>Last week the league plays. Default is the final regular-season week.</summary>
    public int LastWeek { get; set; }

    /// <summary>Point value used when no <c>PointRules</c> row matches. 1..100, default 10.</summary>
    public int DefaultPointValue { get; set; }

    public Guid CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Set by a job once now is past the end of the <see cref="LastWeek"/> window.</summary>
    public bool IsComplete { get; set; }
}
