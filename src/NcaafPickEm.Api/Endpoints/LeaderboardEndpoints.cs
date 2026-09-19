using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Leaderboard;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Season and week leaderboards and the week grid (Feature 07, P5-03). Handlers are thin:
/// membership comes from <c>RequireLeagueMember()</c> and every rule lives in
/// <see cref="LeaderboardService"/>.
/// </summary>
public static class LeaderboardEndpoints
{
    /// <summary>Maps the leaderboard routes under <c>/api/leagues/{leagueId}</c>.</summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapLeaderboardEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder league = api.MapGroup("/leagues/{leagueId:guid}").WithTags("leaderboard");

        league.MapGet("/leaderboard", GetSeasonAsync)
            .WithName("LeaderboardSeason")
            .RequireLeagueMember();

        league.MapGet("/weeks/{week:int}/leaderboard", GetWeekAsync)
            .WithName("LeaderboardWeek")
            .RequireLeagueMember();

        league.MapGet("/weeks/{week:int}/grid", GetGridAsync)
            .WithName("LeaderboardGrid")
            .RequireLeagueMember();

        return api;
    }

    private static async Task<Ok<SeasonLeaderboard>> GetSeasonAsync(
        HttpContext httpContext,
        LeaderboardService leaderboardService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();

        SeasonLeaderboard response = await leaderboardService.GetSeasonAsync(
            membership.LeagueId, membership.Id, cancellationToken);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<WeekLeaderboard>, ProblemHttpResult>> GetWeekAsync(
        int week,
        HttpContext httpContext,
        LeaderboardService leaderboardService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekLeaderboard response = await leaderboardService.GetWeekAsync(
                membership.LeagueId, week, membership.Id, cancellationToken);
            return TypedResults.Ok(response);
        }
        catch (GameSetRuleViolation violation)
        {
            return violation.ToProblem();
        }
    }

    private static async Task<Results<Ok<WeekGrid>, ProblemHttpResult>> GetGridAsync(
        int week,
        HttpContext httpContext,
        LeaderboardService leaderboardService,
        CancellationToken cancellationToken)
    {
        Membership membership = httpContext.GetMembership();
        try
        {
            WeekGrid response = await leaderboardService.GetGridAsync(
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
}
