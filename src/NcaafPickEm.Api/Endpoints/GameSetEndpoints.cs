using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Game-set configuration (Feature 02, P3-03). Handlers are thin: membership is resolved by
/// <c>RequireLeagueMember()</c>/<c>RequireLeagueCommissioner()</c> and every rule and query lives
/// in <see cref="GameSetService"/>.
/// </summary>
public static class GameSetEndpoints
{
    /// <summary>Maps the game-set routes under <c>/api/leagues/{leagueId}/...</c> and the
    /// candidate-search route under <c>/api/seasons/{year}/weeks/{week}/games</c>.</summary>
    public static RouteGroupBuilder MapGameSetEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder leagues = api.MapGroup("/leagues").WithTags("gamesets");
        RouteGroupBuilder league = leagues.MapGroup("/{leagueId:guid}");

        league.MapGet("/gameset-rules", GetDefaultRulesAsync)
            .WithName("GameSetDefaultRulesGet")
            .RequireLeagueCommissioner();

        league.MapPut("/gameset-rules", PutDefaultRulesAsync)
            .WithName("GameSetDefaultRulesPut")
            .RequireLeagueCommissioner();

        league.MapGet("/weeks/{week:int}/gameset-rules", GetWeekRulesAsync)
            .WithName("GameSetWeekRulesGet")
            .RequireLeagueCommissioner();

        league.MapPut("/weeks/{week:int}/gameset-rules", PutWeekRulesAsync)
            .WithName("GameSetWeekRulesPut")
            .RequireLeagueCommissioner();

        league.MapPost("/weeks/{week:int}/gameset/preview", PreviewAsync)
            .WithName("GameSetPreview")
            .RequireLeagueCommissioner();

        league.MapPost("/weeks/{week:int}/gameset/generate", GenerateAsync)
            .WithName("GameSetGenerate")
            .RequireLeagueCommissioner();

        league.MapPost("/weeks/{week:int}/gameset/games", AddGameAsync)
            .WithName("GameSetAddGame")
            .RequireLeagueCommissioner()
            .AddEndpointFilter<ValidationFilter<AddGameRequest>>();

        league.MapDelete("/weeks/{week:int}/gameset/games/{gameId:guid}", RemoveGameAsync)
            .WithName("GameSetRemoveGame")
            .RequireLeagueCommissioner();

        league.MapGet("/weeks/{week:int}/gameset", GetWeekGameSetAsync)
            .WithName("GameSetGet")
            .RequireLeagueMember();

        RouteGroupBuilder seasonWeeks = api.MapGroup("/seasons/{year:int}/weeks/{week:int}").WithTags("gamesets");
        seasonWeeks.MapGet("/games", SearchCandidatesAsync)
            .WithName("GameSetCandidateSearch")
            .RequireAnyLeagueCommissioner();

        return api;
    }

    private static async Task<Results<Ok<GameSetRuleDto[]>, ProblemHttpResult>> GetDefaultRulesAsync(
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        GameSetRuleDto[] rules = await gameSetService.GetDefaultRulesAsync(membership.LeagueId, cancellationToken);
        return TypedResults.Ok(rules);
    }

    private static async Task<Results<Ok<GameSetRuleDto[]>, ProblemHttpResult, ValidationProblem>> PutDefaultRulesAsync(
        GameSetRuleDto[] rules,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            GameSetRuleDto[] result = await gameSetService.ReplaceDefaultRulesAsync(membership.LeagueId, rules, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (GameSetRuleConfigurationException validation)
        {
            return TypedResults.ValidationProblem(validation.Errors);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekRulesResponse>, ProblemHttpResult>> GetWeekRulesAsync(
        int week,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekRulesResponse response = await gameSetService.GetWeekRulesAsync(membership.LeagueId, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekRulesResponse>, ProblemHttpResult, ValidationProblem>> PutWeekRulesAsync(
        int week,
        WeekRulesResponse request,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekRulesResponse response = await gameSetService.ReplaceWeekRulesAsync(
                membership.LeagueId, week, request.UsesOverride, request.Rules, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleConfigurationException validation)
        {
            return TypedResults.ValidationProblem(validation.Errors);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<GameSetPreview>, ProblemHttpResult>> PreviewAsync(
        int week,
        GameSetRuleDto[] candidateRules,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            GameSetPreview preview = await gameSetService.PreviewAsync(membership.LeagueId, week, candidateRules, cancellationToken);
            return TypedResults.Ok(preview);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekGameSetResponse>, ProblemHttpResult>> GenerateAsync(
        int week,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekGameSetResponse response = await gameSetService.GenerateAsync(membership.LeagueId, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekGameSetResponse>, ProblemHttpResult>> AddGameAsync(
        int week,
        AddGameRequest request,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekGameSetResponse response = await gameSetService.AddGameAsync(membership, week, request.GameId, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekGameSetResponse>, ProblemHttpResult>> RemoveGameAsync(
        int week,
        Guid gameId,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekGameSetResponse response = await gameSetService.RemoveGameAsync(membership, week, gameId, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekGameSetResponse>, ProblemHttpResult>> GetWeekGameSetAsync(
        int week,
        HttpContext httpContext,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekGameSetResponse response = await gameSetService.GetWeekGameSetAsync(membership.LeagueId, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Ok<GameCandidate[]>> SearchCandidatesAsync(
        int year,
        int week,
        string? search,
        GameSetService gameSetService,
        CancellationToken cancellationToken)
    {
        GameCandidate[] candidates = await gameSetService.SearchCandidatesAsync(year, week, search, cancellationToken);
        return TypedResults.Ok(candidates);
    }
}
