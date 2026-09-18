using NcaafPickEm.Domain.Points;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Thrown by <see cref="PointRuleService.ReplaceRulesAsync"/> when
/// <see cref="PointRuleValidation.Validate"/> reports one or more problems. Mapped to a 400
/// <c>ValidationProblem</c> by the endpoint layer, keyed by the same <c>Field</c> names
/// <see cref="PointRuleError"/> carries.
/// </summary>
public sealed class PointRuleValidationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PointRuleValidationException(IReadOnlyList<PointRuleError> errors)
        : base("One or more point rules are invalid.")
    {
        Errors = errors;
    }

    /// <summary>Every problem <see cref="PointRuleValidation.Validate"/> found.</summary>
    public IReadOnlyList<PointRuleError> Errors { get; }

    /// <summary>Groups <see cref="Errors"/> the way <c>ValidationFilter&lt;T&gt;</c> keys FluentValidation failures.</summary>
    public Dictionary<string, string[]> ToErrorDictionary() =>
        Errors
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
}
