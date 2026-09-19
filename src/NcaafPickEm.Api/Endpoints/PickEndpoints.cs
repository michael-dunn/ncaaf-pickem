using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Weekly picks (Feature 04, P4-01). Handlers are thin: membership comes from
/// <c>RequireLeagueMember()</c>/<c>RequireLeagueCommissioner()</c> and every rule lives in
/// <see cref="PickService"/>.
/// </summary>
public static class PickEndpoints
{
    /// <summary>Maps the pick routes under <c>/api/leagues/{leagueId}/weeks/{week}/picks...</c>.</summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapPickEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder leagues = api.MapGroup("/leagues").WithTags("picks");
        RouteGroupBuilder league = leagues.MapGroup("/{leagueId:guid}");

        league.MapGet("/weeks/{week:int}/picks/me", GetMyPicksAsync)
            .WithName("PicksGetMine")
            .RequireLeagueMember();

        league.MapPut("/weeks/{week:int}/picks/me/{gameId:guid}", SetPickAsync)
            .WithName("PicksSet")
            .RequireLeagueMember()
            .AddEndpointFilter<ValidationFilter<SetPickRequest>>();

        league.MapPost("/weeks/{week:int}/picks/me/submit", SubmitAsync)
            .WithName("PicksSubmit")
            .RequireLeagueMember();

        league.MapPost("/weeks/{week:int}/picks/me/ack-changes", AckChangesAsync)
            .WithName("PicksAckChanges")
            .RequireLeagueMember();

        league.MapGet("/weeks/{week:int}/picks", GetAllPicksAsync)
            .WithName("PicksGetAll")
            .RequireLeagueMember();

        league.MapGet("/weeks/{week:int}/picks/status", GetStatusRosterAsync)
            .WithName("PicksStatusRoster")
            .RequireLeagueCommissioner();

        return api;
    }

    private static async Task<Results<Ok<MyPicksResponse>, ProblemHttpResult>> GetMyPicksAsync(
        int week,
        HttpContext httpContext,
        PickService pickService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            MyPicksResponse response = await pickService.GetMyPicksAsync(membership, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
        catch (PickRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<MyPicksResponse>, ProblemHttpResult>> SetPickAsync(
        int week,
        Guid gameId,
        SetPickRequest request,
        HttpContext httpContext,
        PickService pickService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Membership membership = httpContext.GetMembership();
        try
        {
            MyPicksResponse response = await pickService.SetPickAsync(
                membership, week, gameId, request.TeamId, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
        catch (PickRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<MyPicksResponse>, ProblemHttpResult>> SubmitAsync(
        int week,
        HttpContext httpContext,
        PickService pickService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            MyPicksResponse response = await pickService.SubmitAsync(membership, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
        catch (PickRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AckChangesAsync(
        int week,
        HttpContext httpContext,
        PickService pickService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            await pickService.AckChangesAsync(membership, week, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekPicksResponse>, ProblemHttpResult>> GetAllPicksAsync(
        int week,
        HttpContext httpContext,
        PickService pickService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekPicksResponse response = await pickService.GetAllPicksAsync(
                membership.LeagueId, week, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
        catch (PickRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<MemberStatusRow[]>, ProblemHttpResult>> GetStatusRosterAsync(
        int week,
        HttpContext httpContext,
        PickService pickService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            MemberStatusRow[] rows = await pickService.GetStatusRosterAsync(
                membership.LeagueId, week, cancellationToken);
            return TypedResults.Ok(rows);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }
}
