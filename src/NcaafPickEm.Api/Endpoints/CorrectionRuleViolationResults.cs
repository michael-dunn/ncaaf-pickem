using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Domain.Scoring;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Maps a <see cref="CorrectionRuleViolation"/> thrown by <c>CorrectionService</c> to the
/// <c>ProblemDetails</c> status 03-API-Contracts.md asks for, the same shape
/// <see cref="GameSetRuleViolationResults"/> uses.
/// </summary>
public static class CorrectionRuleViolationResults
{
    /// <summary>Builds the <c>ProblemHttpResult</c> for <paramref name="violation"/>.</summary>
    public static ProblemHttpResult ToProblem(this CorrectionRuleViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        int statusCode = violation.Code switch
        {
            CorrectionRuleViolationCode.GameNotFound => StatusCodes.Status404NotFound,
            CorrectionRuleViolationCode.NotLocked => StatusCodes.Status409Conflict,
            CorrectionRuleViolationCode.AlreadyVoided => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return TypedResults.Problem(
            title: violation.Code.ToString(),
            detail: violation.Message,
            statusCode: statusCode);
    }
}
