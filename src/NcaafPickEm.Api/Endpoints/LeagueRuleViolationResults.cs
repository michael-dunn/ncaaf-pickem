using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Maps a <see cref="LeagueRuleViolation"/> thrown by a service to the <c>ProblemDetails</c>
/// status 03-API-Contracts.md asks for: 400 for a request that is shaped wrong, 409 for a request
/// that conflicts with the league's current state.
/// </summary>
public static class LeagueRuleViolationResults
{
    /// <summary>Builds the <c>ProblemHttpResult</c> for <paramref name="violation"/>.</summary>
    public static ProblemHttpResult ToProblem(this LeagueRuleViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        int statusCode = violation.Code switch
        {
            LeagueRuleViolationCode.InvalidName => StatusCodes.Status400BadRequest,
            LeagueRuleViolationCode.InvalidWeekRange => StatusCodes.Status400BadRequest,
            LeagueRuleViolationCode.InvalidTransferTarget => StatusCodes.Status400BadRequest,
            LeagueRuleViolationCode.LeagueFull => StatusCodes.Status409Conflict,
            LeagueRuleViolationCode.CannotActOnSelf => StatusCodes.Status409Conflict,
            LeagueRuleViolationCode.LastCommissioner => StatusCodes.Status409Conflict,
            LeagueRuleViolationCode.DisplayNameTaken => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return TypedResults.Problem(
            title: violation.Code.ToString(),
            detail: violation.Message,
            statusCode: statusCode);
    }
}
