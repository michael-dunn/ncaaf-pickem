using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Points;

/// <summary>
/// One point rule as <see cref="PointValueResolver"/> sees it: the persisted
/// <see cref="PointRule"/> flattened to the fields matching needs, so unsaved candidate rules
/// resolve exactly like saved ones.
/// </summary>
/// <param name="Priority">Lower wins. Top of the commissioner's list is 0.</param>
/// <param name="RuleType">Which matching arm applies.</param>
/// <param name="ConferenceId">Conference for <see cref="PointRuleType.ConferenceGame"/>; null means
/// any conference game.</param>
/// <param name="TeamId">Team for <see cref="PointRuleType.Team"/>.</param>
/// <param name="SpreadThreshold">Absolute points for <see cref="PointRuleType.CloseSpread"/>.</param>
/// <param name="PointValue">Value the rule assigns, 1..100.</param>
/// <param name="RuleId">The persisted rule's id, or null for a candidate rule that has never been
/// saved. Only ever echoed back in <see cref="PointResolution.MatchedRuleId"/>.</param>
public readonly record struct PointRuleInfo(
    int Priority,
    PointRuleType RuleType,
    Guid? ConferenceId,
    Guid? TeamId,
    decimal? SpreadThreshold,
    int PointValue,
    Guid? RuleId = null);
