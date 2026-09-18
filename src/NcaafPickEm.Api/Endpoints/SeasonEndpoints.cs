using Microsoft.AspNetCore.Http.HttpResults;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Seasons;
using DomainSeasonWeek = NcaafPickEm.Domain.Seasons.SeasonWeek;
using SeasonWeekDto = NcaafPickEm.Shared.Contracts.Seasons.SeasonWeek;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>
/// Season calendar (Feature 13). The client needs week numbers and their windows to build week
/// pickers and to show "Season starts Week N".
/// </summary>
public static class SeasonEndpoints
{
    /// <summary>Maps <c>/api/seasons/*</c>.</summary>
    /// <param name="api">The <c>/api</c> group.</param>
    public static RouteGroupBuilder MapSeasonEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder seasons = api.MapGroup("/seasons")
            .WithTags("seasons");
        seasons.MapGet("/{year:int}/weeks", GetWeeks)
            .WithName("GetSeasonWeeks")
            .RequireAuthorization(PolicyNames.Authenticated);

        return api;
    }

    /// <summary>
    /// Every week of a season, ordered by week. 404 when the calendar has no such season.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<SeasonWeekDto>>, NotFound>> GetWeeks(
        int year,
        ISeasonWeekSource weekSource,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DomainSeasonWeek> weeks = await weekSource
            .GetWeeksAsync(year, cancellationToken)
            .ConfigureAwait(false);

        if (weeks.Count == 0)
        {
            return TypedResults.NotFound();
        }

        IReadOnlyList<SeasonWeekDto> body = [.. weeks
            .OrderBy(week => week.Week)
            .Select(week => new SeasonWeekDto(week.Week, week.StartUtc, week.EndUtc, week.IsRegularSeason))];

        return TypedResults.Ok(body);
    }
}
