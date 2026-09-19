using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Hosting;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-05 / D-160: <c>App__BehindProxy</c> and the <c>X-Forwarded-*</c> handling the Docker
/// deployment depends on.
/// </summary>
/// <remarks>
/// Two things break silently without it, and both are asserted here. The per-IP rate limiter
/// (D-153) partitions on <c>Connection.RemoteIpAddress</c>, which behind Tailscale Serve is the
/// Docker gateway for every member at once — so one phone signing in would spend the family's
/// whole 30/minute <c>/auth/*</c> budget. And the Google <c>redirect_uri</c> is built from the
/// request's own scheme and host, which is plain <c>http</c> on the container's loopback port,
/// so Google would reject the callback the operator registered as <c>https</c>.
/// <para>
/// Both are proved through real HTTP against the real pipeline; the limiter is turned on with a
/// three-request window, exactly as <c>RateLimitTests</c> does.
/// </para>
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class ForwardedHeadersTests : IAsyncLifetime
{
    private const int Permit = 3;
    private const string LimitedRoute = "/auth/login/google?returnUrl=/";

    private readonly ApiTestFixture _fixture;
    private ProxiedFactory? _behindProxy;
    private ProxiedFactory? _direct;

    public ForwardedHeadersTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _behindProxy = new ProxiedFactory(_fixture.Database.ConnectionString, behindProxy: true);
        _direct = new ProxiedFactory(_fixture.Database.ConnectionString, behindProxy: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _behindProxy!.DisposeAsync();
        await _direct!.DisposeAsync();
    }

    [Fact]
    public async Task GivenBehindProxy_WhenTwoMembersArriveThroughTheSameProxy_ThenEachHasItsOwnWindow()
    {
        using HttpClient client = CreateClient(_behindProxy!);

        // One member burns the whole window.
        for (int attempt = 0; attempt < Permit; attempt++)
        {
            await ExpectAsync(client, "203.0.113.10", HttpStatusCode.Redirect);
        }

        await ExpectAsync(client, "203.0.113.10", HttpStatusCode.TooManyRequests);

        // A second member behind the same proxy is untouched by the first one's spending.
        for (int attempt = 0; attempt < Permit; attempt++)
        {
            await ExpectAsync(client, "203.0.113.11", HttpStatusCode.Redirect);
        }
    }

    [Fact]
    public async Task GivenNotBehindProxy_WhenRequestsCarryXForwardedFor_ThenTheHeaderIsIgnored()
    {
        using HttpClient client = CreateClient(_direct!);

        // Distinct forwarded addresses, but the flag is off, so nothing reads them and every
        // request lands in the same partition - which is also why believing the header by
        // default would be a spoofing hole rather than a convenience.
        for (int attempt = 0; attempt < Permit; attempt++)
        {
            await ExpectAsync(client, $"203.0.113.{attempt + 20}", HttpStatusCode.Redirect);
        }

        await ExpectAsync(client, "203.0.113.99", HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GivenBehindProxy_WhenTheProxyTerminatesTls_ThenTheGoogleRedirectUriIsHttps()
    {
        using HttpClient client = CreateClient(_behindProxy!);

        using var request = new HttpRequestMessage(HttpMethod.Get, LimitedRoute);
        request.Headers.Add("X-Forwarded-For", "203.0.113.30");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "pickem.tailnet-1234.ts.net");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        string location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://accounts.google.com/");
        Uri.UnescapeDataString(location)
            .Should().Contain(
                "redirect_uri=https://pickem.tailnet-1234.ts.net/auth/callback/google",
                "the challenge must name the HTTPS callback the operator registered with Google");
    }

    [Fact]
    public async Task GivenNotBehindProxy_WhenTheProxyTerminatesTls_ThenTheRedirectUriStaysOnTheRequestScheme()
    {
        using HttpClient client = CreateClient(_direct!);

        using var request = new HttpRequestMessage(HttpMethod.Get, LimitedRoute);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "pickem.tailnet-1234.ts.net");

        using HttpResponseMessage response = await client.SendAsync(request);

        // The negative half of the test above: the flag, not the header, decides.
        Uri.UnescapeDataString(response.Headers.Location!.ToString())
            .Should().Contain("redirect_uri=http://localhost/auth/callback/google");
    }

    private static HttpClient CreateClient(ProxiedFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task ExpectAsync(HttpClient client, string forwardedFor, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LimitedRoute);
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expected, "X-Forwarded-For was {0}", forwardedFor);
    }

    /// <summary>The app with the limiter on, a three-per-minute window, and a chosen proxy mode.</summary>
    private sealed class ProxiedFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly bool _behindProxy;

        public ProxiedFactory(string connectionString, bool behindProxy)
        {
            _connectionString = connectionString;
            _behindProxy = behindProxy;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jobs:Enabled", "false");
            builder.UseSetting("Providers:ReferenceData", "Fixture");
            builder.UseSetting("Providers:LiveScores", "Fixture");
            builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");

            builder.UseSetting(ForwardedHeadersSetup.BehindProxyKey, _behindProxy ? "true" : "false");

            builder.UseSetting(RateLimitingSetup.EnabledKey, "true");
            builder.UseSetting("RateLimiting:AuthPermitPerMinute", Permit.ToString());
            builder.UseSetting("RateLimiting:InvitePermitPerMinute", Permit.ToString());
        }
    }
}
