using NcaafPickEm.Shared.Contracts.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IPushApi"/> for screenshotting <see cref="Components.NotificationSettings"/>
/// without a real Api or a browser that can actually grant push permission. Server state is one
/// dictionary of subscribed endpoints; there is no VAPID key validation, no delivery and no
/// Development/Testing gate on the test endpoint (the fake has no environment to check).
/// </summary>
public sealed class FakePushApi : IPushApi
{
    /// <summary>Fake VAPID public key, just non-null so the "unavailable" state does not trigger.</summary>
    public const string FakeVapidPublicKey = "fake-vapid-public-key-for-screenshots";

    private readonly HashSet<string> _serverSubscriptions = [];

    /// <summary>
    /// Set from the page's <c>?simulate=</c> query flag so the P7-02 screenshots can force the
    /// 503 "server unavailable" state without a second fake type.
    /// </summary>
    public bool SimulateUnavailable { get; set; }

    /// <inheritdoc />
    public Task<string?> GetVapidPublicKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(SimulateUnavailable ? null : FakeVapidPublicKey);

    /// <inheritdoc />
    public Task SubscribeAsync(PushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        _serverSubscriptions.Add(request.Endpoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        _serverSubscriptions.Remove(endpoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> GetStatusAsync(string endpoint, CancellationToken cancellationToken = default) =>
        Task.FromResult(_serverSubscriptions.Contains(endpoint));

    /// <inheritdoc />
    public Task<NotificationResult> SendTestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(NotificationResult.Sent);
}
