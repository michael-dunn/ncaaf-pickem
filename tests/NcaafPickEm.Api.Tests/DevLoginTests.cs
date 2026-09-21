using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET /auth/dev-login</c> (P2-05): signs in as a fixture demo user with no tailnet identity, so a UI or
/// manual check can run against the fixture data with no OAuth client.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class DevLoginTests
{
    private static readonly WebApplicationFactoryClientOptions HttpsNoRedirects = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    private readonly ApiTestFixture _fixture;

    public DevLoginTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheDemoLeagueIsSeeded_WhenDevLoggingIn_ThenTheSessionSignsInAsThatUser()
    {
        using WebApplicationFactory<Program> app = WithDemoLeagueSeeded();
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage login = await client.GetAsync("/auth/dev-login?user=michael");
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().Be("/");

        using HttpResponseMessage me = await client.GetAsync("/api/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        MeResponse? body = await me.Content.ReadFromJsonAsync<MeResponse>();
        body!.DisplayName.Should().Be("Michael");
        body.Email.Should().Be("michael@fixture.local");
    }

    [Fact]
    public async Task GivenAReturnUrl_WhenDevLoggingIn_ThenItIsHonoured()
    {
        using WebApplicationFactory<Program> app = WithDemoLeagueSeeded();
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage login =
            await client.GetAsync("/auth/dev-login?user=alyson&returnUrl=/leagues/mine");

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().Be("/leagues/mine");
    }

    [Fact]
    public async Task GivenAnUnknownFixtureUser_WhenDevLoggingIn_ThenItIs404()
    {
        using WebApplicationFactory<Program> app = WithDemoLeagueSeeded();
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage response = await client.GetAsync("/auth/dev-login?user=nobody");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenNoUserQueryString_WhenDevLoggingIn_ThenItIs400()
    {
        using WebApplicationFactory<Program> app = WithDemoLeagueSeeded();
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage response = await client.GetAsync("/auth/dev-login");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The cookie-scheme app, with the demo league seeded on this host's own startup (the shared
    /// fixture factories boot with <c>Seed:DemoLeague</c> unset/false).
    /// </summary>
    private WebApplicationFactory<Program> WithDemoLeagueSeeded() =>
        _fixture.CookieFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("Seed:DemoLeague", "true"));
}
