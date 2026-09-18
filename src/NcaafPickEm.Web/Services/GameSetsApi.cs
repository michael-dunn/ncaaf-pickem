using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IGameSetsApi"/>, calling the routes in 03-API-Contracts.md over the shared
/// <see cref="HttpClient"/> (see <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class GameSetsApi(HttpClient httpClient) : IGameSetsApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<GameSetRuleDto[]> GetDefaultRulesAsync(
        Guid leagueId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/gameset-rules", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<GameSetRuleDto[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task<GameSetRuleDto[]> SaveDefaultRulesAsync(
        Guid leagueId, GameSetRuleDto[] rules, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.PutAsJsonAsync($"api/leagues/{leagueId}/gameset-rules", rules, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<GameSetRuleDto[]>(response) ?? rules;
    }

    /// <inheritdoc />
    public async Task<WeekRulesResponse> GetWeekRulesAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/weeks/{week}/gameset-rules", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekRulesResponse>(response);
    }

    /// <inheritdoc />
    public async Task<WeekRulesResponse> SaveWeekRulesAsync(
        Guid leagueId, int week, WeekRulesResponse request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset-rules", request, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekRulesResponse>(response);
    }

    /// <inheritdoc />
    public async Task<GameSetPreview> PreviewAsync(
        Guid leagueId, int week, GameSetRuleDto[] candidateRules, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/preview", candidateRules, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<GameSetPreview>(response);
    }

    /// <inheritdoc />
    public async Task<WeekGameSetResponse> GenerateAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/generate", content: null, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekGameSetResponse>(response);
    }

    /// <inheritdoc />
    public async Task<WeekGameSetResponse> AddGameAsync(
        Guid leagueId, int week, Guid gameId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/games", new AddGameRequest(gameId), cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekGameSetResponse>(response);
    }

    /// <inheritdoc />
    public async Task<WeekGameSetResponse> RemoveGameAsync(
        Guid leagueId, int week, Guid gameId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(
            $"api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekGameSetResponse>(response);
    }

    /// <inheritdoc />
    public async Task<WeekGameSetResponse> GetWeekGameSetAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/weeks/{week}/gameset", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<WeekGameSetResponse>(response);
    }

    /// <inheritdoc />
    public async Task<GameCandidate[]> SearchCandidatesAsync(
        int seasonYear, int week, string? search, CancellationToken cancellationToken = default)
    {
        string query = string.IsNullOrWhiteSpace(search) ? string.Empty : $"?search={WebUtility.UrlEncode(search)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"api/seasons/{seasonYear}/weeks/{week}/games{query}", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<GameCandidate[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task<ConferenceDto[]> GetConferencesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/reference/conferences", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<ConferenceDto[]>(response) ?? [];
    }

    /// <inheritdoc />
    public async Task<TeamDto[]> SearchTeamsAsync(string? search, CancellationToken cancellationToken = default)
    {
        string query = string.IsNullOrWhiteSpace(search) ? string.Empty : $"?search={WebUtility.UrlEncode(search)}";
        using HttpResponseMessage response = await _httpClient.GetAsync($"api/reference/teams{query}", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadAsync<TeamDto[]>(response) ?? [];
    }
}
