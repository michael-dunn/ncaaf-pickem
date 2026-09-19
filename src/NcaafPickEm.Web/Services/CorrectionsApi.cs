using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="ICorrectionsApi"/>, calling the routes in 03-API-Contracts.md ("Scoring and
/// corrections", Feature 06) over the shared <see cref="HttpClient"/> (see
/// <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class CorrectionsApi(HttpClient httpClient) : ICorrectionsApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<GameSetGameDto> OverrideResultAsync(
        Guid leagueId, int week, Guid gameId, Guid winnerTeamId, string reason, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/override-result",
            new OverrideResultRequest(winnerTeamId, reason),
            cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<GameSetGameDto>(response);
    }

    /// <inheritdoc />
    public async Task<GameSetGameDto> VoidGameAsync(
        Guid leagueId, int week, Guid gameId, string reason, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/void",
            new VoidGameRequest(reason),
            cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<GameSetGameDto>(response);
    }

    /// <inheritdoc />
    public async Task<AuditEntry[]> GetAuditAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/audit", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<AuditEntry[]>(response) ?? [];
    }
}
