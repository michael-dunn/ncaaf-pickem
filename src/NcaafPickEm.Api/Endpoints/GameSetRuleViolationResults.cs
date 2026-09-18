using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Domain.GameSets;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Maps a <see cref="GameSetRuleViolation"/> thrown by <c>GameSetService</c>/<c>PointRuleService</c>
/// to the <c>ProblemDetails</c> status 03-API-Contracts.md asks for.
/// </summary>
public static class GameSetRuleViolationResults
{
    /// <summary>Builds the <c>ProblemHttpResult</c> for <paramref name="violation"/>.</summary>
    public static ProblemHttpResult ToProblem(this GameSetRuleViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        int statusCode = violation.Code switch
        {
            GameSetRuleViolationCode.WeekOutOfRange => StatusCodes.Status404NotFound,
            GameSetRuleViolationCode.GameNotFound => StatusCodes.Status404NotFound,
            GameSetRuleViolationCode.Locked => StatusCodes.Status409Conflict,
            GameSetRuleViolationCode.ExceedsMax => StatusCodes.Status409Conflict,
            GameSetRuleViolationCode.GameNotEligible => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        Dictionary<string, object?>? extensions = violation.Count is int count
            ? new Dictionary<string, object?> { ["count"] = count }
            : null;

        return TypedResults.Problem(
            title: violation.Code.ToString(),
            detail: violation.Message,
            statusCode: statusCode,
            extensions: extensions);
    }
}
