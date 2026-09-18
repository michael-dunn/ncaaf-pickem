using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Points;

/// <summary>
/// The outcome of <see cref="PointValueResolver.Resolve"/>: the value to store in
/// <c>WeekGameSetGames.ResolvedPointValue</c>, plus why it is that value.
/// </summary>
/// <param name="Value">Points the game is worth.</param>
/// <param name="Source">Override, rule, or league default.</param>
/// <param name="MatchedRuleId">The rule that decided, when the source is
/// <see cref="PointValueSource.Rule"/> and that rule is a saved one; null otherwise.</param>
public readonly record struct PointResolution(
    int Value,
    PointValueSource Source,
    Guid? MatchedRuleId);
