using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Points;

/// <summary>
/// Checks a league's point configuration before it is saved (Feature 03): every point value is
/// 1..100, every rule carries the field its type needs, and no two rules share a priority. Pure,
/// and separate from <see cref="PointValueResolver"/>, which resolves whatever it is handed.
/// </summary>
public static class PointRuleValidation
{
    /// <summary>Field name used for the league default in a returned <see cref="PointRuleError"/>.</summary>
    public const string DefaultPointValueField = "DefaultPointValue";

    /// <summary>
    /// Validates the league default and a full, ordered set of point rules.
    /// </summary>
    /// <param name="rules">The rules as the commissioner ordered them. Error field names index into
    /// this list.</param>
    /// <param name="leagueDefault">The league's default point value.</param>
    /// <returns>Every problem found, in list order. Empty means the configuration is legal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public static IReadOnlyList<PointRuleError> Validate(
        IReadOnlyList<PointRuleInfo> rules,
        int leagueDefault)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var errors = new List<PointRuleError>();

        if (!PointValueLimits.IsValid(leagueDefault))
        {
            errors.Add(new PointRuleError(DefaultPointValueField, OutOfRangeMessage(leagueDefault)));
        }

        var seenPriorities = new HashSet<int>();

        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];

            if (!PointValueLimits.IsValid(rule.PointValue))
            {
                errors.Add(new PointRuleError(
                    Field(index, nameof(PointRuleInfo.PointValue)),
                    OutOfRangeMessage(rule.PointValue)));
            }

            if (!seenPriorities.Add(rule.Priority))
            {
                errors.Add(new PointRuleError(
                    Field(index, nameof(PointRuleInfo.Priority)),
                    $"Priority {rule.Priority} is used by more than one rule. Priorities must be unique."));
            }

            ValidateRuleType(rule, index, errors);
        }

        return errors;
    }

    private static void ValidateRuleType(PointRuleInfo rule, int index, List<PointRuleError> errors)
    {
        switch (rule.RuleType)
        {
            case PointRuleType.ConferenceGame:
                // A null ConferenceId is legal: the rule then matches any conference game.
                break;

            case PointRuleType.CloseSpread:
                if (rule.SpreadThreshold is not { } threshold)
                {
                    errors.Add(new PointRuleError(
                        Field(index, nameof(PointRuleInfo.SpreadThreshold)),
                        "A close-spread rule needs a spread threshold."));
                }
                else if (threshold <= 0)
                {
                    errors.Add(new PointRuleError(
                        Field(index, nameof(PointRuleInfo.SpreadThreshold)),
                        $"Spread threshold must be greater than 0, but was {threshold}."));
                }

                break;

            case PointRuleType.Team:
                if (rule.TeamId is null || rule.TeamId == Guid.Empty)
                {
                    errors.Add(new PointRuleError(
                        Field(index, nameof(PointRuleInfo.TeamId)),
                        "A team rule needs a team."));
                }

                break;

            default:
                errors.Add(new PointRuleError(
                    Field(index, nameof(PointRuleInfo.RuleType)),
                    $"{rule.RuleType} is not a point rule type."));
                break;
        }
    }

    private static string Field(int index, string name) =>
        $"Rules[{index}].{name}";

    private static string OutOfRangeMessage(int pointValue) =>
        $"Point value must be between {PointValueLimits.Min} and {PointValueLimits.Max}, but was {pointValue}.";
}
