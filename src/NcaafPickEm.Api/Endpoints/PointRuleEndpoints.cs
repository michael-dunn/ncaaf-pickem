using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Point-value configuration (Feature 03, P3-03). Every rule and query lives in
/// <see cref="PointRuleService"/>.
/// </summary>
public static class PointRuleEndpoints
{
    /// <summary>Maps <c>/api/leagues/{leagueId}/point-rules</c> and the per-game override route.</summary>
    public static RouteGroupBuilder MapPointRuleEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder leagues = api.MapGroup("/leagues").WithTags("points");
        RouteGroupBuilder league = leagues.MapGroup("/{leagueId:guid}");

        league.MapGet("/point-rules", GetRulesAsync)
            .WithName("PointRulesGet")
            .RequireLeagueCommissioner();

        league.MapPut("/point-rules", PutRulesAsync)
            .WithName("PointRulesPut")
            .RequireLeagueCommissioner();

        league.MapPut("/weeks/{week:int}/gameset/games/{gameId:guid}/points", SetOverrideAsync)
            .WithName("PointOverridePut")
            .RequireLeagueCommissioner()
            .AddEndpointFilter<ValidationFilter<SetPointOverrideRequest>>();

        return api;
    }

    private static async Task<Ok<PointRuleDto[]>> GetRulesAsync(
        HttpContext httpContext,
        PointRuleService pointRuleService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        PointRuleDto[] rules = await pointRuleService.GetRulesAsync(membership.LeagueId, cancellationToken);
        return TypedResults.Ok(rules);
    }

    private static async Task<Results<Ok<PointRuleDto[]>, ValidationProblem>> PutRulesAsync(
        PointRuleDto[] rules,
        HttpContext httpContext,
        PointRuleService pointRuleService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            PointRuleDto[] result = await pointRuleService.ReplaceRulesAsync(membership.LeagueId, rules, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (PointRuleValidationException validation)
        {
            return TypedResults.ValidationProblem(validation.ToErrorDictionary());
        }
    }

    private static async Task<Results<Ok<GameSetGameDto>, ProblemHttpResult>> SetOverrideAsync(
        int week,
        Guid gameId,
        SetPointOverrideRequest request,
        HttpContext httpContext,
        PointRuleService pointRuleService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            GameSetGameDto response = await pointRuleService.SetOverrideAsync(
                membership.LeagueId, week, gameId, request.PointValue, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }
}
