using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Domain.Picks;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Maps a <see cref="PickRuleViolation"/> thrown by <c>PickService</c> to the
/// <c>ProblemDetails</c> status 03-API-Contracts.md asks for. The <c>title</c> is the code name,
/// matching <see cref="GameSetRuleViolationResults"/>.
/// </summary>
public static class PickRuleViolationResults
{
    /// <summary>Builds the <c>ProblemHttpResult</c> for <paramref name="violation"/>.</summary>
    /// <param name="violation">The violation the service threw.</param>
    public static ProblemHttpResult ToProblem(this PickRuleViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        int statusCode = violation.Code switch
        {
            PickRuleViolationCode.TeamNotInGame => StatusCodes.Status400BadRequest,
            PickRuleViolationCode.PicksNotVisible => StatusCodes.Status403Forbidden,
            PickRuleViolationCode.GameNotInSet => StatusCodes.Status404NotFound,
            PickRuleViolationCode.WeekNotCurrent => StatusCodes.Status409Conflict,
            PickRuleViolationCode.Locked => StatusCodes.Status409Conflict,
            PickRuleViolationCode.GameNotActive => StatusCodes.Status409Conflict,
            PickRuleViolationCode.IncompletePicks => StatusCodes.Status409Conflict,
            PickRuleViolationCode.NoGamesInSet => StatusCodes.Status409Conflict,
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
