using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IMeApi"/>, calling <c>/api/me</c> over the shared <see cref="HttpClient"/>
/// (CSRF header and 401 redirect handlers already attached, see <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class MeApi(HttpClient httpClient) : IMeApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("api/me", cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync(response);
    }

    /// <inheritdoc />
    public async Task<MeResponse> UpdateDisplayNameAsync(
        UpdateMeRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync("api/me", request, cancellationToken);
        await EnsureSuccessAsync(response);
        return await ReadRequiredAsync(response);
    }

    // Same error-shape handling as LeaguesApi (03-API-Contracts.md: every failure is a
    // ProblemDetails body); kept local rather than shared to avoid a cross-cutting base class for
    // two small clients.
    private static async Task<MeResponse> ReadRequiredAsync(HttpResponseMessage response)
    {
        MeResponse? value = await response.Content.ReadFromJsonAsync<MeResponse>();
        return value ?? throw new LeaguesApiException((int)response.StatusCode, "The server sent an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ProblemDetailsBody? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Not every failure response is a ProblemDetails body (e.g. a 401 with no content).
        }

        string message = problem?.Title
            ?? problem?.Detail
            ?? $"Request failed with status {(int)response.StatusCode}.";

        throw new LeaguesApiException((int)response.StatusCode, message, problem?.Errors);
    }
}
