using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Infrastructure.Push;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// How <see cref="WebPushSender"/> maps the transport's answers onto <see cref="PushSendResult"/>
/// (Feature 11 delivery policy). No database and no network: the WebPush client takes an
/// <see cref="HttpClient"/>, so a stub handler is the whole seam.
/// </summary>
public sealed class WebPushSenderTests
{
    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task GivenAnAcceptedPush_WhenSending_ThenItSucceeds(HttpStatusCode status)
    {
        PushSendResult result = await SendAsync(status);

        result.Success.Should().BeTrue();
        result.SubscriptionGone.Should().BeFalse();
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.Gone, 410)]
    public async Task GivenAnExpiredSubscription_WhenSending_ThenItReportsGone(HttpStatusCode status, int expected)
    {
        PushSendResult result = await SendAsync(status);

        result.Success.Should().BeFalse();
        result.SubscriptionGone.Should().BeTrue("404 and 410 mean the subscription must be deleted");
        result.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GivenAnyOtherFailure_WhenSending_ThenItIsRetryable(HttpStatusCode status)
    {
        PushSendResult result = await SendAsync(status);

        result.Success.Should().BeFalse();
        result.SubscriptionGone.Should().BeFalse("only 404/410 delete the subscription");
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GivenUnusableSubscriptionKeys_WhenSending_ThenItReportsGoneRatherThanRetrying()
    {
        // The payload cannot be encrypted for this device, ever; retrying the same keys is futile.
        PushSendResult result = await SendAsync(
            HttpStatusCode.Created,
            subscription => subscription.P256dh = "not-a-p256-public-key");

        result.Success.Should().BeFalse();
        result.SubscriptionGone.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAnUnreachablePushService_WhenSending_ThenItIsRetryable()
    {
        PushSendResult result = await SendAsync(
            HttpStatusCode.Created,
            configure: null,
            handler: new ThrowingHandler(new HttpRequestException("no route to host")));

        result.Success.Should().BeFalse();
        result.SubscriptionGone.Should().BeFalse();
        result.Error.Should().Contain("Could not reach");
    }

    [Fact]
    public void GivenAGeneratedKeyPair_WhenValidating_ThenItIsConfigured()
    {
        (string publicKey, string privateKey) = VapidKeyGenerator.Generate();

        VapidConfiguration configuration = VapidConfiguration.FromOptions(new PushOptions
        {
            VapidPublicKey = publicKey,
            VapidPrivateKey = privateKey,
            Subject = "mailto:commissioner@example.com",
        });

        configuration.IsConfigured.Should().BeTrue();
        configuration.PublicKey.Should().Be(publicKey);
        configuration.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("<dev-vapid-public-key>", "<dev-vapid-private-key>", "mailto:you@example.com")]
    [InlineData("BKn3", "trQm", "mailto:you@example.com")]
    public void GivenMissingOrPlaceholderKeys_WhenValidating_ThenItIsNotConfigured(
        string? publicKey,
        string? privateKey,
        string? subject)
    {
        // The template ships placeholders; the app must boot with them and report the problem at
        // the edge rather than refusing to start (P7-01 card).
        VapidConfiguration configuration = VapidConfiguration.FromOptions(new PushOptions
        {
            VapidPublicKey = publicKey,
            VapidPrivateKey = privateKey,
            Subject = subject,
        });

        configuration.IsConfigured.Should().BeFalse();
        configuration.PublicKey.Should().BeNull();
        configuration.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GivenNoKeys_WhenTheNullSenderSends_ThenItFailsWithTheReason()
    {
        VapidConfiguration configuration = VapidConfiguration.FromOptions(new PushOptions());
        var sender = new NullPushSender(configuration, NullLogger<NullPushSender>.Instance);

        PushSendResult result = await sender.SendAsync(
            NewSubscription(),
            new PushPayload("t", "b", "/u", "tag"),
            PushTtl.Reminder,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.SubscriptionGone.Should().BeFalse("a missing key pair is the server's fault, not the device's");
        result.Error.Should().Contain("Push:Vapid");
    }

    private static async Task<PushSendResult> SendAsync(
        HttpStatusCode status,
        Action<PushSubscription>? configure = null,
        HttpMessageHandler? handler = null)
    {
        (string publicKey, string privateKey) = VapidKeyGenerator.Generate();

        VapidConfiguration configuration = VapidConfiguration.FromOptions(new PushOptions
        {
            VapidPublicKey = publicKey,
            VapidPrivateKey = privateKey,
            Subject = "mailto:commissioner@example.com",
        });

        var sender = new WebPushSender(
            new StubHttpClientFactory(handler ?? new StatusHandler(status)),
            configuration,
            NullLogger<WebPushSender>.Instance);

        PushSubscription subscription = NewSubscription();
        configure?.Invoke(subscription);

        return await sender.SendAsync(
            subscription,
            new PushPayload("Title", "Body", "/leagues/x/picks", "tag"),
            PushTtl.Reminder,
            CancellationToken.None);
    }

    /// <summary>A subscription whose keys are real enough for the library to encrypt for.</summary>
    private static PushSubscription NewSubscription()
    {
        using ECDiffieHellman client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = client.ExportParameters(includePrivateParameters: false);

        byte[] uncompressedPoint = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];

        return new PushSubscription
        {
            Id = Guid.CreateVersion7(),
            Endpoint = "https://push.example/send/abc",
            P256dh = Base64Url(uncompressedPoint),
            Auth = Base64Url(RandomNumberGenerator.GetBytes(16)),
        };
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        // WebPushClient disposes the HttpClient it is given, so each call gets its own wrapper
        // over the one (undisposed) handler.
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StatusHandler(HttpStatusCode status)
        {
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }
}
