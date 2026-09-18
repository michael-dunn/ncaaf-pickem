using NcaafPickEm.Shared.Contracts.Seasons;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the season calendar route in 03-API-Contracts.md ("Season calendar",
/// Feature 13). This endpoint already exists on <c>main</c> (P0-05); it is split from
/// <see cref="ILeaguesApi"/> because it is a different feature's contract.
/// </summary>
public interface ISeasonsApi
{
    /// <summary>
    /// <c>GET /api/seasons/{year}/weeks</c>. Throws <see cref="LeaguesApiException"/> with
    /// <c>StatusCode == 404</c> when the calendar has no such season.
    /// </summary>
    Task<SeasonWeek[]> GetWeeksAsync(int year, CancellationToken cancellationToken = default);
}
