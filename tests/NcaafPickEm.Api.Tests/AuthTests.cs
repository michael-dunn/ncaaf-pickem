using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Feature 08 sign-in, over the shipped cookie and Google pipeline with only Google's network
/// replaced (<see cref="FakeGoogleBackchannel"/>).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AuthTests
{
    private static readonly WebApplicationFactoryClientOptions HttpsNoRedirects = new()
    {
        // The session and correlation cookies are Secure, so the client has to speak https or the
        // cookie container silently drops them.
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    private readonly ApiTestFixture _fixture;

    public AuthTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnonymousCaller_WhenGettingMe_ThenItIsUnauthorized()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenNewGoogleAccount_WhenSigningIn_ThenTheUserIsCreatedFromTheGoogleProfile()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";
        using WebApplicationFactory<Program> app = WithGoogle(subject, "first.timer@example.com", "First Timer");
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        MeResponse me = await SignInThroughGoogleAsync(client);

        me.Email.Should().Be("first.timer@example.com");
        me.DisplayName.Should().Be("First Timer");
        me.Leagues.Should().BeEmpty();

        Guid storedId = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Users.SingleAsync(user => user.GoogleSubject == subject)).Id);

        storedId.Should().Be(me.UserId);
    }

    [Fact]
    public async Task GivenAGoogleProfileNameOver30Characters_WhenSigningIn_ThenTheDisplayNameIsTrimmed()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";
        const string longName = "Bartholomew Montgomery Fitzwilliam the Third";

        using WebApplicationFactory<Program> app = WithGoogle(subject, "long.name@example.com", longName);
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        MeResponse me = await SignInThroughGoogleAsync(client);

        me.DisplayName.Should().Be("Bartholomew Montgomery Fitzwil");
        me.DisplayName.Length.Should().BeLessThanOrEqualTo(30);
    }

    [Fact]
    public async Task GivenAReturningAccount_WhenSigningInAgain_ThenItMatchesBySubjectAndDoesNotDuplicate()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";

        using WebApplicationFactory<Program> first = WithGoogle(subject, "returning@example.com", "Returning");
        using HttpClient firstClient = first.CreateClient(HttpsNoRedirects);
        MeResponse before = await SignInThroughGoogleAsync(firstClient);

        // Same Google subject, a display name the user has since changed on their own, and a new
        // browser session: matching must still land on the same row (Feature 08).
        await _fixture.Factory.ExecuteDbAsync(async database =>
        {
            Domain.Users.User user = await database.Users.SingleAsync(candidate => candidate.Id == before.UserId);
            user.DisplayName = "Chosen Name";
            await database.SaveChangesAsync();
        });

        using WebApplicationFactory<Program> second = WithGoogle(subject, "returning@example.com", "Different Google Name");
        using HttpClient secondClient = second.CreateClient(HttpsNoRedirects);
        MeResponse after = await SignInThroughGoogleAsync(secondClient);

        after.UserId.Should().Be(before.UserId);
        after.DisplayName.Should().Be("Chosen Name");

        int count = await _fixture.Factory.QueryDbAsync(database =>
            database.Users.CountAsync(user => user.GoogleSubject == subject));
        count.Should().Be(1);
    }

    [Fact]
    public async Task GivenASignedInSession_WhenLoggingOut_ThenTheCookieIsClearedAndMeIsUnauthorized()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";
        using WebApplicationFactory<Program> app = WithGoogle(subject, "signs.out@example.com", "Signs Out");
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        await SignInThroughGoogleAsync(client);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        using HttpResponseMessage logoutResponse = await client.SendAsync(logout);

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        logoutResponse.Headers.GetValues("Set-Cookie")
            .Should().Contain(header => header.StartsWith($"{AuthDefaults.CookieName}=;", StringComparison.Ordinal));

        using HttpResponseMessage afterLogout = await client.GetAsync("/api/me");
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenAnAbsoluteReturnUrl_WhenChallengingGoogle_ThenSignInLandsOnTheSiteRoot()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";
        using WebApplicationFactory<Program> app = WithGoogle(subject, "redirect@example.com", "Redirect");
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage challenge =
            await client.GetAsync("/auth/login/google?returnUrl=https://evil.example.com/steal");
        using HttpResponseMessage callback = await FollowCallbackAsync(client, challenge);

        callback.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task GivenARelativeReturnUrl_WhenSigningIn_ThenItIsHonoured()
    {
        string subject = $"google-sub-{Guid.CreateVersion7():N}";
        using WebApplicationFactory<Program> app = WithGoogle(subject, "deep.link@example.com", "Deep Link");
        using HttpClient client = app.CreateClient(HttpsNoRedirects);

        using HttpResponseMessage challenge =
            await client.GetAsync("/auth/login/google?returnUrl=/leagues/mine");
        using HttpResponseMessage callback = await FollowCallbackAsync(client, challenge);

        callback.Headers.Location!.OriginalString.Should().Be("/leagues/mine");
    }

    /// <summary>
    /// The cookie-scheme app with Google's backchannel replaced by a fixed profile.
    /// </summary>
    private WebApplicationFactory<Program> WithGoogle(string subject, string email, string? name) =>
        _fixture.CookieFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<GoogleOptions>(
                    GoogleDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.ClientId = "test-client-id";
                        options.ClientSecret = "test-client-secret";
                        options.BackchannelHttpHandler = new FakeGoogleBackchannel(subject, email, name);
                    })));

    private static async Task<MeResponse> SignInThroughGoogleAsync(HttpClient client)
    {
        using HttpResponseMessage challenge = await client.GetAsync("/auth/login/google?returnUrl=/");
        using HttpResponseMessage callback = await FollowCallbackAsync(client, challenge);

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using HttpResponseMessage me = await client.GetAsync("/api/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await me.Content.ReadFromJsonAsync<MeResponse>())!;
    }

    /// <summary>
    /// Plays Google's part: take the <c>state</c> we were challenged with and come back to the
    /// callback with it plus an authorization code.
    /// </summary>
    private static async Task<HttpResponseMessage> FollowCallbackAsync(
        HttpClient client,
        HttpResponseMessage challenge)
    {
        challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);

        Uri googleUrl = challenge.Headers.Location!;
        googleUrl.Host.Should().Be("accounts.google.com");

        string? state = HttpUtility.ParseQueryString(googleUrl.Query)["state"];
        state.Should().NotBeNullOrEmpty();

        return await client.GetAsync(
            $"{AuthDefaults.GoogleCallbackPath}?code=fake-authorization-code&state={Uri.EscapeDataString(state!)}");
    }
}
