using NcaafPickEm.Shared.Contracts.Leaderboard;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="ILeaderboardApi"/>, calling the routes in 03-API-Contracts.md over the shared
/// <see cref="HttpClient"/> (see <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class LeaderboardApi(HttpClient httpClient) : ILeaderboardApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<SeasonLeaderboard> GetSeasonLeaderboardAsync(
        Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/leaderboard", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<SeasonLeaderboard>(response);
    }

    /// <inheritdoc />
    public async Task<WeekLeaderboard> GetWeekLeaderboardAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/weeks/{week}/leaderboard", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekLeaderboard>(response);
    }

    /// <inheritdoc />
    public async Task<WeekGrid> GetWeekGridAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/weeks/{week}/grid", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekGrid>(response);
    }
}
