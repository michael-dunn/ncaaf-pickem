using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using WebPush;
using WebPushSubscription = WebPush.PushSubscription;

namespace NcaafPickEm.Infrastructure.Push;

/// <summary>
/// The real transport: VAPID-signed, encrypted web push via the <c>WebPush</c> package
/// (web-push-libs), per 01-Architecture.md.
/// </summary>
public sealed class WebPushSender : IPushSender
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for outbound pushes.</summary>
    public const string HttpClientName = "web-push";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VapidConfiguration _vapid;
    private readonly ILogger<WebPushSender> _logger;

    /// <summary>Creates the sender.</summary>
    /// <param name="httpClientFactory">Supplies the pooled outbound client.</param>
    /// <param name="vapid">The validated key pair.</param>
    /// <param name="logger">Log sink.</param>
    public WebPushSender(
        IHttpClientFactory httpClientFactory,
        VapidConfiguration vapid,
        ILogger<WebPushSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _vapid = vapid;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PushSendResult> SendAsync(
        Domain.Notifications.PushSubscription subscription,
        PushPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(payload);

        if (_vapid.Details is not VapidDetails details)
        {
            // Registration only picks this sender when the keys validate, so this is a belt-and-
            // braces path; it still must not throw (the card: "send reports Failed without throwing").
            return PushSendResult.Failed(_vapid.Error ?? "Web push is not configured.");
        }

        var target = new WebPushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);

        var options = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            // The library validates option names against exactly this set: headers, gcmAPIKey,
            // vapidDetails, TTL. "TTL" is case-sensitive.
            ["vapidDetails"] = details,
            ["TTL"] = (int)Math.Clamp(ttl.TotalSeconds, 0, int.MaxValue),
        };

        using HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var client = new WebPushClient(httpClient);

        try
        {
            await client.SendNotificationAsync(target, payload.ToJson(), options, cancellationToken);
            return PushSendResult.Accepted();
        }
        catch (WebPushException exception)
        {
            int statusCode = (int)exception.StatusCode;

            if (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                _logger.LogInformation(
                    "Push subscription {SubscriptionId} is gone ({StatusCode}); it will be deleted",
                    subscription.Id,
                    statusCode);

                return PushSendResult.Gone(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Push service returned {statusCode}; the subscription no longer exists."),
                    statusCode);
            }

            _logger.LogWarning(
                exception,
                "Push to subscription {SubscriptionId} failed with {StatusCode}",
                subscription.Id,
                statusCode);

            return PushSendResult.Failed(
                string.Create(CultureInfo.InvariantCulture, $"Push service returned {statusCode}: {exception.Message}"),
                statusCode);
        }
        catch (WebPush.Model.InvalidEncryptionDetailsException exception)
        {
            // The stored p256dh/auth cannot encrypt for this subscription. Retrying the same keys
            // can never succeed, so this counts as gone rather than as a transient failure.
            _logger.LogWarning(
                exception,
                "Push subscription {SubscriptionId} has unusable keys; it will be deleted",
                subscription.Id);

            return PushSendResult.Gone($"Subscription keys are unusable: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Push to subscription {SubscriptionId} could not reach the push service", subscription.Id);
            return PushSendResult.Failed($"Could not reach the push service: {exception.Message}");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Push to subscription {SubscriptionId} timed out", subscription.Id);
            return PushSendResult.Failed("The push service did not respond in time.");
        }
    }
}
