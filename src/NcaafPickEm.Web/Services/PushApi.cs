using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Shared.Contracts.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Real <see cref="IPushApi"/>, calling <c>/api/push/*</c> over the shared <see cref="HttpClient"/>
/// (CSRF header and 401 redirect handlers already attached, see <see cref="DependencyInjection"/>).
/// </summary>
/// <param name="httpClient">The app's scoped API client.</param>
public sealed class PushApi(HttpClient httpClient) : IPushApi
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<string?> GetVapidPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync("api/push/vapid-public-key", cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return null;
        }

        await ApiResponses.EnsureSuccessAsync(response);
        VapidPublicKeyResponse body = await ApiResponses.ReadRequiredAsync<VapidPublicKeyResponse>(response);
        return body.PublicKey;
    }

    /// <inheritdoc />
    public async Task SubscribeAsync(PushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await _httpClient.PostAsJsonAsync("api/push/subscriptions", request, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, "api/push/subscriptions")
        {
            Content = JsonContent.Create(new DeletePushSubscriptionRequest(endpoint)),
        };
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
    }

    /// <inheritdoc />
    public async Task<bool> GetStatusAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        string url = $"api/push/status?endpoint={Uri.EscapeDataString(endpoint)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        PushStatusResponse body = await ApiResponses.ReadRequiredAsync<PushStatusResponse>(response);
        return body.HasSubscriptionForThisDevice;
    }

    /// <inheritdoc />
    public async Task<NotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync("api/push/test", null, cancellationToken);
        await ApiResponses.EnsureSuccessAsync(response);
        return await ApiResponses.ReadRequiredAsync<NotificationResult>(response);
    }
}
