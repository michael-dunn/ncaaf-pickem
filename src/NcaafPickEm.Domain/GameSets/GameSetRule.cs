using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// One rule that selects games into a league's weekly game set (Feature 02). Rules are unioned,
/// never intersected; <see cref="SortOrder"/> is display only.
/// </summary>
/// <remarks>
/// <see cref="Week"/> null is the league's default configuration. Non-null rows are a week
/// override, used only while that week's <c>WeekGameSets.UsesOverride</c> is set.
/// </remarks>
public sealed class GameSetRule
{
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    /// <summary>Null for the default configuration; a week number for an override.</summary>
    public int? Week { get; set; }

    public GameSetRuleType RuleType { get; set; }

    public Guid? ConferenceId { get; set; }

    public Conference? Conference { get; set; }

    public Guid? TeamId { get; set; }

    public Team? Team { get; set; }

    /// <summary>Only meaningful for <see cref="GameSetRuleType.Conference"/>.</summary>
    public bool ConferenceGamesOnly { get; set; }

    public int SortOrder { get; set; }
}
