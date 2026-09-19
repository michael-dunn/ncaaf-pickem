using NcaafPickEm.Domain.Notifications;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// The web-push transport: one encrypted message to one device (Feature 11).
/// </summary>
/// <remarks>
/// Deliberately free of policy. Retries, subscription cleanup and <c>NotificationLog</c> rows are
/// <see cref="NotificationService"/>'s job; an implementation only says what happened. Callers may
/// assume it does not throw for a delivery failure — only a cancelled
/// <see cref="CancellationToken"/> escapes.
/// </remarks>
public interface IPushSender
{
    /// <summary>Sends one notification to one subscription.</summary>
    /// <param name="subscription">The device's stored subscription.</param>
    /// <param name="payload">What the service worker will show.</param>
    /// <param name="ttl">
    /// How long the push service may hold the message for an offline device. One hour for the
    /// weekly reminders (see <see cref="PushTtl"/>).
    /// </param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    Task<PushSendResult> SendAsync(
        PushSubscription subscription,
        PushPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken);
}
