using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Points;

/// <summary>
/// One rule that assigns a point value to a game in a set (Feature 03). Lower
/// <see cref="Priority"/> wins; the first matching rule decides and the rest are ignored.
/// </summary>
public sealed class PointRule
{
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    /// <summary>Lower is higher priority. Top of the commissioner's list is 0.</summary>
    public int Priority { get; set; }

    public PointRuleType RuleType { get; set; }

    public Guid? ConferenceId { get; set; }

    public Conference? Conference { get; set; }

    public Guid? TeamId { get; set; }

    public Team? Team { get; set; }

    /// <summary>Only meaningful for <see cref="PointRuleType.CloseSpread"/>; absolute points.</summary>
    public decimal? SpreadThreshold { get; set; }

    /// <summary>1..100.</summary>
    public int PointValue { get; set; }
}
