using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.TeamForm;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// The picks page's recent form tray (Feature 17). Handlers are thin: membership comes from
/// <c>RequireLeagueMember()</c> and every rule lives in <see cref="TeamFormService"/>.
/// </summary>
public static class TeamFormEndpoints
{
    /// <summary>Maps <c>GET /api/leagues/{leagueId}/weeks/{week}/team-form</c>.</summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapTeamFormEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder league = api.MapGroup("/leagues/{leagueId:guid}").WithTags("team-form");

        league.MapGet("/weeks/{week:int}/team-form", GetWeekTeamFormAsync)
            .WithName("TeamFormWeek")
            .RequireLeagueMember();

        return api;
    }

    private static async Task<Results<Ok<WeekTeamFormResponse>, ProblemHttpResult>> GetWeekTeamFormAsync(
        int week,
        HttpContext httpContext,
        TeamFormService teamFormService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekTeamFormResponse response = await teamFormService.GetWeekTeamFormAsync(
                membership.LeagueId, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }
}
