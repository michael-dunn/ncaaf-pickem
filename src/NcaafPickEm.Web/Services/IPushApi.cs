using NcaafPickEm.Shared.Contracts.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for <c>/api/push/*</c> (Feature 11, P7-02). Pages depend on this interface,
/// never on <see cref="HttpClient"/> directly, matching <see cref="IMeApi"/>'s pattern.
/// </summary>
public interface IPushApi
{
    /// <summary>
    /// <c>GET /api/push/vapid-public-key</c>. Returns <see langword="null"/> for the 503
    /// "notifications are not configured on this server" response (D-073) instead of throwing -
    /// that is an expected state <see cref="Components.NotificationSettings"/> renders, not a
    /// failure.
    /// </summary>
    Task<string?> GetVapidPublicKeyAsync(CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/push/subscriptions</c>. Upsert by endpoint.</summary>
    Task SubscribeAsync(PushSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>DELETE /api/push/subscriptions</c>. Idempotent.</summary>
    Task UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/push/status?endpoint=</c>. True when the server still has a subscription for
    /// this device against the caller's account.
    /// </summary>
    Task<bool> GetStatusAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/push/test</c>. Development/Testing only, commissioner of any league; a 403 or
    /// 404 (route not mapped, e.g. in Production) surfaces as <see cref="LeaguesApiException"/>.
    /// </summary>
    Task<NotificationResult> SendTestAsync(CancellationToken cancellationToken = default);
}
