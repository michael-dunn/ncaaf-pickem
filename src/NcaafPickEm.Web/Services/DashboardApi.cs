using NcaafPickEm.Shared.Contracts.Dashboard;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IDashboardApi"/>, calling the route in 03-API-Contracts.md over the shared
/// <see cref="HttpClient"/> (see <see cref="DependencyInjection"/>). Written against P6-02's
/// contract before that endpoint landed (P6-03 depends on P6-02) - the shape below matches
/// <c>03-API-Contracts.md</c>'s "Influence dashboard" section exactly, so no client change is
/// expected once P6-02 merges.
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class DashboardApi(HttpClient httpClient) : IDashboardApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<DashboardResponse> GetDashboardAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync($"api/leagues/{leagueId}/weeks/{week}/dashboard", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DashboardResponse>(response);
    }
}
