using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Validation;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;
using LeagueWeekDto = NcaafPickEm.Shared.Contracts.Seasons.LeagueWeek;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Leagues and members (Feature 01, P1-01). Handlers are thin: membership is resolved once by
/// <c>RequireLeagueMember()</c>/<c>RequireLeagueCommissioner()</c> and read back with
/// <c>GetMembership()</c>; every rule and every query lives in <see cref="LeagueService"/>.
/// </summary>
public static class LeagueEndpoints
{
    /// <summary>Maps <c>/api/leagues/*</c> except the invite routes (see <c>InviteEndpoints</c>).</summary>
    public static RouteGroupBuilder MapLeagueEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder leagues = api.MapGroup("/leagues")
            .WithTags("leagues")
            .RequireAuthorization(PolicyNames.Authenticated);

        leagues.MapPost("", CreateAsync)
            .WithName("LeagueCreate")
            .AddEndpointFilter<ValidationFilter<CreateLeagueRequest>>();

        leagues.MapGet("", ListMineAsync)
            .WithName("LeagueListMine");

        RouteGroupBuilder league = leagues.MapGroup("/{leagueId:guid}");

        league.MapGet("", GetDetailAsync)
            .WithName("LeagueGet")
            .RequireLeagueMember();

        league.MapGet("/members", GetMembersAsync)
            .WithName("LeagueMembers")
            .RequireLeagueMember();

        league.MapPut("/members/me/display-name", SetDisplayNameAsync)
            .WithName("LeagueSetMyDisplayName")
            .RequireLeagueMember()
            .AddEndpointFilter<ValidationFilter<SetLeagueDisplayNameRequest>>();

        league.MapGet("/weeks", GetWeeksAsync)
            .WithName("LeagueWeeks")
            .RequireLeagueMember();

        league.MapPut("/settings", UpdateSettingsAsync)
            .WithName("LeagueUpdateSettings")
            .RequireLeagueCommissioner()
            .AddEndpointFilter<ValidationFilter<UpdateLeagueSettingsRequest>>();

        league.MapDelete("/members/{membershipId:guid}", RemoveMemberAsync)
            .WithName("LeagueRemoveMember")
            .RequireLeagueCommissioner();

        league.MapPost("/members/{membershipId:guid}/promote", PromoteAsync)
            .WithName("LeaguePromoteMember")
            .RequireLeagueCommissioner();

        league.MapPost("/members/{membershipId:guid}/demote", DemoteAsync)
            .WithName("LeagueDemoteMember")
            .RequireLeagueCommissioner();

        league.MapPost("/commissioner/transfer", TransferAsync)
            .WithName("LeagueTransferCommissioner")
            .RequireLeagueCommissioner()
            .AddEndpointFilter<ValidationFilter<TransferRequest>>();

        return api;
    }

    private static async Task<Results<Ok<LeagueDetail>, ProblemHttpResult>> CreateAsync(
        CreateLeagueRequest request,
        ICurrentUser currentUser,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        try
        {
            LeagueDetail detail = await leagueService.CreateAsync(currentUser.UserId, request, cancellationToken);
            return TypedResults.Ok(detail);
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Ok<LeagueSummary[]>> ListMineAsync(
        ICurrentUser currentUser,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        LeagueSummary[] summaries = await leagueService.ListMineAsync(currentUser.UserId, cancellationToken);
        return TypedResults.Ok(summaries);
    }

    private static async Task<Ok<LeagueDetail>> GetDetailAsync(
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        LeagueDetail detail = await leagueService.GetDetailAsync(membership, cancellationToken);
        return TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<LeagueDetail>, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateLeagueSettingsRequest request,
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            LeagueDetail detail = await leagueService.UpdateSettingsAsync(membership, request, cancellationToken);
            return TypedResults.Ok(detail);
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Ok<MemberRow[]>> GetMembersAsync(
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        bool includeStatus = membership.Role == MembershipRole.Commissioner;
        MemberRow[] rows = await leagueService.GetMembersAsync(membership.LeagueId, includeStatus, cancellationToken);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<MemberRow>, ProblemHttpResult>> SetDisplayNameAsync(
        SetLeagueDisplayNameRequest request,
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            MemberRow row = await leagueService.SetDisplayNameAsync(membership, request, cancellationToken);
            return TypedResults.Ok(row);
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveMemberAsync(
        Guid membershipId,
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership caller = httpContext.GetMembership();
        try
        {
            await leagueService.RemoveMemberAsync(caller, membershipId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PromoteAsync(
        Guid membershipId,
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership caller = httpContext.GetMembership();
        try
        {
            await leagueService.PromoteAsync(caller, membershipId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DemoteAsync(
        Guid membershipId,
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership caller = httpContext.GetMembership();
        try
        {
            await leagueService.DemoteAsync(caller, membershipId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> TransferAsync(
        TransferRequest request,
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership caller = httpContext.GetMembership();
        try
        {
            await leagueService.TransferAsync(caller, request, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Ok<LeagueWeekDto[]>> GetWeeksAsync(
        HttpContext httpContext,
        LeagueService leagueService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        LeagueWeekDto[] weeks = await leagueService.GetLeagueWeeksAsync(membership, cancellationToken);
        return TypedResults.Ok(weeks);
    }
}
