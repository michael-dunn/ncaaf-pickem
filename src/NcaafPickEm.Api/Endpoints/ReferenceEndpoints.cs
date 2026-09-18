using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Endpoints;

/// <summary>FBS reference data (conferences and teams) for pickers across the app (P3-03).</summary>
public static class ReferenceEndpoints
{
    private const int MaxTeamResults = 50;

    /// <summary>Maps <c>/api/reference/*</c>.</summary>
    public static RouteGroupBuilder MapReferenceEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        RouteGroupBuilder reference = api.MapGroup("/reference")
            .WithTags("reference")
            .RequireAuthorization(PolicyNames.Authenticated);

        reference.MapGet("/conferences", GetConferencesAsync)
            .WithName("ReferenceConferences");

        reference.MapGet("/teams", GetTeamsAsync)
            .WithName("ReferenceTeams");

        return api;
    }

    private static async Task<Ok<ConferenceDto[]>> GetConferencesAsync(
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        List<Conference> conferences = await database.Conferences
            .AsNoTracking()
            .Where(c => c.Classification == TeamClassification.Fbs)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        ConferenceDto[] dtos = [.. conferences.Select(c => new ConferenceDto(c.Id, c.Name, c.Abbreviation))];
        return TypedResults.Ok(dtos);
    }

    private static async Task<Ok<TeamDto[]>> GetTeamsAsync(
        string? search,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        IQueryable<Team> query = database.Teams
            .AsNoTracking()
            .Where(t => t.Classification == TeamClassification.Fbs);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string needle = search.Trim();
            query = query.Where(t =>
                EF.Functions.Like(t.School, $"%{needle}%")
                || (t.Abbreviation != null && EF.Functions.Like(t.Abbreviation, $"%{needle}%")));
        }

        List<Team> teams = await query
            .OrderBy(t => t.School)
            .Take(MaxTeamResults)
            .ToListAsync(cancellationToken);

        TeamDto[] dtos = [.. teams.Select(t => new TeamDto(t.Id, t.School, t.Abbreviation, t.ConferenceId, t.LogoUrl))];
        return TypedResults.Ok(dtos);
    }
}
