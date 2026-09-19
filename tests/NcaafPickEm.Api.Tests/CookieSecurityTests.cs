using System.Net;
using System.Net.Http.Headers;
using System.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01: the session cookie's flags and the shape of an unauthenticated refusal.
/// </summary>
/// <remarks>
/// Feature 08 asks for <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Lax</c> and a 90-day sliding
/// session; 03-API-Contracts.md adds "unauthenticated <c>/api/*</c> = 401 (not a redirect)".
/// Both the configured options and the header actually written on the wire are checked, because
/// the two can drift (an option set after the handler is built would not reach the cookie).
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class CookieSecurityTests
{
    private static readonly WebApplicationFactoryClientOptions HttpsNoRedirects = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false,
    };

    private readonly ApiTestFixture _fixture;

    public CookieSecurityTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GivenTheCookieScheme_WhenConfigured_ThenItMatchesFeature08()
    {
        CookieAuthenticationOptions options = _fixture.Factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.Cookie.Name.Should().Be("ncaaf.auth");
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        options.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        options.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(90));
        options.SlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public async Task GivenARealSignIn_WhenTheCookieIsIssued_ThenTheHeaderCarriesEveryFlag()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";

        using WebApplicationFactory<Program> app = _fixture.CookieFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<GoogleOptions>(
                    GoogleDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.ClientId = "test-client-id";
                        options.ClientSecret = "test-client-secret";
                        options.BackchannelHttpHandler =
                            new FakeGoogleBackchannel(subject, "cookie.flags@example.com", "Cookie Flags");
                    })));

        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage challenge = await client.GetAsync("/auth/login/google?returnUrl=/");
        string state = HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"]!;

        // HandleCookies is off, so the correlation cookie has to be replayed by hand.
        using var callback = new HttpRequestMessage(
            HttpMethod.Get,
            $"{AuthDefaults.GoogleCallbackPath}?code=fake-authorization-code&state={Uri.EscapeDataString(state)}");

        foreach (string correlation in challenge.Headers.GetValues("Set-Cookie"))
        {
            callback.Headers.Add("Cookie", correlation.Split(';')[0]);
        }

        using HttpResponseMessage response = await client.SendAsync(callback);

        string sessionCookie = response.Headers.GetValues("Set-Cookie")
            .Single(header => header.StartsWith($"{AuthDefaults.CookieName}=", StringComparison.Ordinal));

        sessionCookie.Should().Contain("httponly", "the SPA must never be able to read the session")
            .And.Contain("secure", "the cookie may only travel over HTTPS")
            .And.Contain("samesite=lax", "a cross-site form post must not carry the session");

        // Persistent (IsPersistent = true) rather than a session cookie, so the phone stays
        // signed in across restarts for the whole 90 days.
        sessionCookie.Should().Contain("expires=");
    }

    [Fact]
    public async Task GivenAnAnonymousApiCall_WhenRefused_ThenItIs401ProblemDetailsAndNotARedirect()
    {
        using HttpClient client = _fixture.Factory.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull("the SPA cannot follow a login redirect from fetch()");

        MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
        contentType?.MediaType.Should().Be("application/problem+json");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":401");
    }

    [Fact]
    public async Task GivenAnAnonymousBrowserNavigation_WhenRefused_ThenItRedirectsToLogin()
    {
        using HttpClient client = _fixture.CookieFactory.CreateClient(HttpsNoRedirects);

        // /auth is the one authorized area outside /api, so it keeps the browser-friendly
        // redirect the SPA's own routes would want.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/login");
        response.Headers.Location.Host.Should().Be("localhost", "the redirect stays on this origin");
    }
}
