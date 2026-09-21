using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IDevToolsApi"/>, calling <c>/api/admin/fixture/*</c> over the shared
/// <see cref="HttpClient"/> (CSRF header and 401 redirect handlers already attached).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class DevToolsApi(HttpClient httpClient) : IDevToolsApi
{
    private const string ClockRoute = "api/admin/fixture/clock";
    private const string DemoRoute = "api/admin/fixture/demo";

    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<DevClockResponse> GetClockAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(ClockRoute, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DevClockResponse>(response);
    }

    /// <inheritdoc />
    public async Task<DevClockResponse> SetClockAsync(DevClockRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(ClockRoute, request, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DevClockResponse>(response);
    }

    /// <inheritdoc />
    public async Task<DevClockResponse> ResetClockAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(ClockRoute, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DevClockResponse>(response);
    }

    /// <inheritdoc />
    public async Task<DemoWeekResponse> GetDemoWeekAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(DemoRoute, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DemoWeekResponse>(response);
    }

    /// <inheritdoc />
    public Task<DemoWeekResponse> GenerateDemoWeekAsync(CancellationToken cancellationToken = default) =>
        PostDemoAsync($"{DemoRoute}/generate", cancellationToken);

    /// <inheritdoc />
    public Task<DemoWeekResponse> FillDemoPicksAsync(bool includeMe, CancellationToken cancellationToken = default) =>
        PostDemoAsync($"{DemoRoute}/picks?includeMe={(includeMe ? "true" : "false")}", cancellationToken);

    /// <inheritdoc />
    public Task<DemoWeekResponse> LockDemoWeekAsync(CancellationToken cancellationToken = default) =>
        PostDemoAsync($"{DemoRoute}/lock", cancellationToken);

    /// <inheritdoc />
    public Task<DemoWeekResponse> PollDemoWeekAsync(int? snapshot, CancellationToken cancellationToken = default) =>
        PostDemoAsync(
            snapshot is int n ? $"{DemoRoute}/poll?snapshot={n}" : $"{DemoRoute}/poll",
            cancellationToken);

    /// <inheritdoc />
    public Task<DemoWeekResponse> ResetDemoWeekAsync(CancellationToken cancellationToken = default) =>
        PostDemoAsync($"{DemoRoute}/reset", cancellationToken);

    private async Task<DemoWeekResponse> PostDemoAsync(string route, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(route, null, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DemoWeekResponse>(response);
    }
}
