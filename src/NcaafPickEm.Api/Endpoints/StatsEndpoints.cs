using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Stats;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// The league stats page and member stats profiles (Feature 14). Handlers are thin: membership
/// comes from <c>RequireLeagueMember()</c> and every rule lives in <see cref="MemberStatsService"/>.
/// </summary>
public static class StatsEndpoints
{
    /// <summary>Maps the stats routes under <c>/api/leagues/{leagueId}</c>.</summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapStatsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder league = api.MapGroup("/leagues/{leagueId:guid}").WithTags("stats");

        league.MapGet("/stats", GetLeagueStatsAsync)
            .WithName("StatsLeague")
            .RequireLeagueMember();

        league.MapGet("/members/{membershipId:guid}/stats", GetMemberProfileAsync)
            .WithName("StatsMember")
            .RequireLeagueMember();

        return api;
    }

    private static async Task<Ok<LeagueStats>> GetLeagueStatsAsync(
        HttpContext httpContext,
        MemberStatsService statsService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();

        LeagueStats response = await statsService.GetLeagueStatsAsync(
            membership.LeagueId, membership.Id, cancellationToken);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<MemberStatsProfile>, ProblemHttpResult>> GetMemberProfileAsync(
        Guid membershipId,
        HttpContext httpContext,
        MemberStatsService statsService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            MemberStatsProfile response = await statsService.GetMemberProfileAsync(
                membership.LeagueId, membershipId, membership.Id, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (LeagueRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }
}
