using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Commissioner corrections and the league audit trail (Feature 06, P5-02). Handlers are thin:
/// membership is resolved by <c>RequireLeagueMember()</c>/<c>RequireLeagueCommissioner()</c> and
/// every rule and query lives in <see cref="CorrectionService"/>.
/// </summary>
public static class CorrectionEndpoints
{
    /// <summary>Maps the correction routes under <c>/api/leagues/{leagueId}/...</c>.</summary>
    public static RouteGroupBuilder MapCorrectionEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder leagues = api.MapGroup("/leagues").WithTags("corrections");
        RouteGroupBuilder league = leagues.MapGroup("/{leagueId:guid}");

        league.MapPost("/weeks/{week:int}/gameset/games/{gameId:guid}/override-result", OverrideResultAsync)
            .WithName("CorrectionOverrideResult")
            .RequireLeagueCommissioner()
            .AddEndpointFilter<ValidationFilter<OverrideResultRequest>>();

        league.MapPost("/weeks/{week:int}/gameset/games/{gameId:guid}/void", VoidGameAsync)
            .WithName("CorrectionVoidGame")
            .RequireLeagueCommissioner()
            .AddEndpointFilter<ValidationFilter<VoidGameRequest>>();

        league.MapGet("/audit", GetAuditAsync)
            .WithName("CorrectionAudit")
            .RequireLeagueMember();

        return api;
    }

    private static async Task<Results<Ok<GameSetGameDto>, ProblemHttpResult>> OverrideResultAsync(
        int week,
        Guid gameId,
        OverrideResultRequest request,
        HttpContext httpContext,
        CorrectionService correctionService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            GameSetGameDto dto = await correctionService.OverrideResultAsync(
                membership, week, gameId, request.WinnerTeamId, request.Reason, cancellationToken);
            return TypedResults.Ok(dto);
        }
        catch (CorrectionRuleViolation violation)
        {
            return violation.ToProblem();
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<GameSetGameDto>, ProblemHttpResult>> VoidGameAsync(
        int week,
        Guid gameId,
        VoidGameRequest request,
        HttpContext httpContext,
        CorrectionService correctionService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            GameSetGameDto dto = await correctionService.VoidGameAsync(
                membership, week, gameId, request.Reason, cancellationToken);
            return TypedResults.Ok(dto);
        }
        catch (CorrectionRuleViolation violation)
        {
            return violation.ToProblem();
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Ok<AuditEntry[]>> GetAuditAsync(
        HttpContext httpContext,
        CorrectionService correctionService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        AuditEntry[] entries = await correctionService.GetAuditAsync(membership.LeagueId, cancellationToken);
        return TypedResults.Ok(entries);
    }
}
