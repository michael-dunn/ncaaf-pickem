using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// One game-set rule, flattened from a <c>GameSetRules</c> row or from an unsaved candidate rule
/// during preview. Rules are unioned, never intersected, and <c>SortOrder</c> is display only so
/// it is not carried here.
/// </summary>
/// <param name="Type">Which kind of rule this is.</param>
/// <param name="ConferenceId">
/// Conference for <see cref="GameSetRuleType.Conference"/>. A conference rule with no conference
/// matches nothing rather than matching everything.
/// </param>
/// <param name="TeamId">
/// Team for <see cref="GameSetRuleType.Team"/>. A team rule with no team matches nothing.
/// </param>
/// <param name="ConferenceGamesOnly">
/// Conference rules only: require both teams in the conference rather than either.
/// </param>
public sealed record RuleInfo(
    GameSetRuleType Type,
    Guid? ConferenceId = null,
    Guid? TeamId = null,
    bool ConferenceGamesOnly = false);
