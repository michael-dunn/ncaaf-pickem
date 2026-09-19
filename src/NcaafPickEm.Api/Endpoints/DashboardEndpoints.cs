using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Dashboard;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// The influence dashboard (Feature 05, P6-02). A member sees only their own - there is no
/// parameter to view it "as" another member, so any query string on the request is refused rather
/// than silently ignored.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>Maps <c>GET /api/leagues/{leagueId}/weeks/{week}/dashboard</c>.</summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGroup("/leagues/{leagueId:guid}")
            .WithTags("dashboard")
            .MapGet("/weeks/{week:int}/dashboard", GetAsync)
            .WithName("DashboardGet")
            .RequireLeagueMember();

        return api;
    }

    private static async Task<Results<Ok<DashboardResponse>, ProblemHttpResult>> GetAsync(
        int week,
        HttpContext httpContext,
        DashboardService dashboardService,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Query.Count > 0)
        {
            return TypedResults.Problem(
                title: "UnsupportedQueryParameter",
                detail: "The dashboard is always the caller's own; it takes no query parameters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        Membership membership = httpContext.GetMembership();
        try
        {
            DashboardResponse response = await dashboardService.GetAsync(membership, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }
}
