using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Notifications;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// The sender used when no usable VAPID key pair is configured: nothing leaves the machine, every
/// attempt is logged and reported as Failed with the reason.
/// </summary>
/// <remarks>
/// This is what keeps "the app boots without <c>Push__*</c>" true (P7-01 card). It is not a test
/// double — tests use their own fake so they can script outcomes; this one exists so a developer
/// who has not generated keys, or a deployment where the operator forgot them, gets a clear line
/// in the log and a <c>NotificationLog</c> row instead of an exception in a background job.
/// </remarks>
public sealed class NullPushSender : IPushSender
{
    private readonly VapidConfiguration _vapid;
    private readonly ILogger<NullPushSender> _logger;

    /// <summary>Creates the sender and warns once that push is off.</summary>
    /// <param name="vapid">The (unconfigured) VAPID configuration, for its error text.</param>
    /// <param name="logger">Log sink.</param>
    public NullPushSender(VapidConfiguration vapid, ILogger<NullPushSender> logger)
    {
        _vapid = vapid;
        _logger = logger;

        _logger.LogWarning(
            "Web push is disabled: {Reason} Notifications will be recorded as Failed until keys are configured.",
            Reason(vapid));
    }

    /// <inheritdoc />
    public Task<PushSendResult> SendAsync(
        PushSubscription subscription,
        PushPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(payload);

        _logger.LogInformation(
            "Dropping push to subscription {SubscriptionId}: web push is not configured",
            subscription.Id);

        return Task.FromResult(PushSendResult.Failed(Reason(_vapid)));
    }

    private static string Reason(VapidConfiguration vapid) =>
        vapid.Error ?? "Push:VapidPublicKey / Push:VapidPrivateKey / Push:Subject are not configured.";
}
