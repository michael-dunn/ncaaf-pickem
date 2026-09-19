using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IPicksApi"/>, calling the routes in 03-API-Contracts.md over the shared
/// <see cref="HttpClient"/> (see <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class PicksApi(HttpClient httpClient) : IPicksApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<MyPicksResponse> GetMyPicksAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/weeks/{week}/picks/me", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<MyPicksResponse>(response);
    }

    /// <inheritdoc />
    public async Task<MyPicksResponse> SetPickAsync(
        Guid leagueId, int week, Guid gameId, Guid teamId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/picks/me/{gameId}",
            new SetPickRequest(teamId),
            cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<MyPicksResponse>(response);
    }

    /// <inheritdoc />
    public async Task<MyPicksResponse> SubmitAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/leagues/{leagueId}/weeks/{week}/picks/me/submit", content: null, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<MyPicksResponse>(response);
    }

    /// <inheritdoc />
    public async Task AckChangesAsync(Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/leagues/{leagueId}/weeks/{week}/picks/me/ack-changes", content: null, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
    }
}
