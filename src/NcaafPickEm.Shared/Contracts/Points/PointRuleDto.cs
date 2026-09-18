using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Points;

/// <summary>One point rule (Feature 03). Lower <paramref name="Priority"/> wins; PUT sends the full ordered list.</summary>
/// <param name="RuleId">Existing rule id, or null for a rule being created.</param>
/// <param name="Priority">Evaluation order, lower first; unique within the league.</param>
/// <param name="RuleType">ConferenceGame, CloseSpread, or Team.</param>
/// <param name="ConferenceId">ConferenceGame rules: null = any conference game.</param>
/// <param name="TeamId">Team rules only.</param>
/// <param name="SpreadThreshold">CloseSpread rules only: matches when |spread| is strictly below this.</param>
/// <param name="PointValue">1..100.</param>
public sealed record PointRuleDto(
    Guid? RuleId,
    int Priority,
    PointRuleType RuleType,
    Guid? ConferenceId,
    Guid? TeamId,
    decimal? SpreadThreshold,
    int PointValue);
