using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IPointRulesApi"/>, calling the routes in 03-API-Contracts.md over the shared
/// <see cref="HttpClient"/> (see <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class PointRulesApi(HttpClient httpClient) : IPointRulesApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<PointRuleDto[]> GetRulesAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/point-rules", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<PointRuleDto[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task<PointRuleDto[]> SaveRulesAsync(
        Guid leagueId, PointRuleDto[] rules, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.PutAsJsonAsync($"api/leagues/{leagueId}/point-rules", rules, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<PointRuleDto[]>(response) ?? rules;
    }

    /// <inheritdoc />
    public async Task<WeekGameSetResponse> SetOverrideAsync(
        Guid leagueId, int week, Guid gameId, int? pointValue, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/points",
            new SetPointOverrideRequest(pointValue),
            cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekGameSetResponse>(response);
    }
}
