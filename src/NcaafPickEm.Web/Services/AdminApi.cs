using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IAdminApi"/>, calling <c>/api/admin/*</c> over the shared
/// <see cref="HttpClient"/> (CSRF header and 401 redirect handlers already attached).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class AdminApi(HttpClient httpClient) : IAdminApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<DataStatusResponse> GetDataStatusAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/admin/data-status", cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<DataStatusResponse>(response);
    }

    /// <inheritdoc />
    public async Task<ManualRefreshResponse> RefreshAsync(
        RefreshDataType dataType,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(
            $"api/admin/refresh/{dataType}", null, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<ManualRefreshResponse>(response);
    }

    /// <inheritdoc />
    public async Task ResolveUnmatchedAsync(
        Guid id,
        ResolveUnmatchedRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/admin/unmatched/{id}/resolve", request, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
    }
}
