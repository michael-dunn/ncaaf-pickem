using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Seasons;

namespace NcaafPickEm.Web.Services;

/// <summary>Real <see cref="ISeasonsApi"/>, calling <c>GET /api/seasons/{year}/weeks</c>.</summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class SeasonsApi(HttpClient httpClient) : ISeasonsApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<SeasonWeek[]> GetWeeksAsync(int year, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/seasons/{year}/weeks", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LeaguesApiException(
                (int)response.StatusCode,
                response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? $"No season calendar for {year}."
                    : $"Request failed with status {(int)response.StatusCode}.");
        }

        SeasonWeek[]? weeks = await response.Content.ReadFromJsonAsync<SeasonWeek[]>(cancellationToken);
        return weeks ?? [];
    }
}
