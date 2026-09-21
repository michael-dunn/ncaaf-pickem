using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Header identity end to end (P9-02): the real <see cref="TailscaleAuthenticationHandler"/>,
/// reached through the policy scheme, with no <c>X-Test-User</c> anywhere.
/// </summary>
/// <remarks>
/// These run against <see cref="ApiTestFixture.CookieFactory"/> — the same app and the same
/// database as every other test, but without <c>TestAuthHandler</c> overriding the default
/// scheme, so the app's own <c>AppAuth</c> selector decides who is signed in. Every test invents
/// its own login, because the database is shared by the whole run and both
/// <c>Users.ExternalSubject</c> and <c>Users.Email</c> are unique.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class TailscaleAuthTests
{
    private readonly ApiTestFixture _fixture;

    public TailscaleAuthTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnUnknownLogin_WhenCallingMe_ThenTheUserIsCreatedFromTheHeaders()
    {
        string login = NewLogin();
        using HttpClient client = CreateClient(login, "Alice Example");

        MeResponse me = await GetMeAsync(client);

        me.Email.Should().Be(login, "the tailnet login is stored verbatim as the email (Q2)");
        me.DisplayName.Should().Be("Alice Example");
        me.Leagues.Should().BeEmpty();

        User stored = await _fixture.Factory.QueryDbAsync(database =>
            database.Users.SingleAsync(user => user.ExternalSubject == login));

        stored.Id.Should().Be(me.UserId);
        stored.Email.Should().Be(login);
    }

    [Fact]
    public async Task GivenNoNameHeader_WhenCallingMe_ThenTheDisplayNameIsTheLoginLocalPart()
    {
        string login = NewLogin();
        using HttpClient client = CreateClient(login, name: null);

        MeResponse me = await GetMeAsync(client);

        me.DisplayName.Should().Be(login.Split('@')[0]);
    }

    [Fact]
    public async Task GivenTheSameLoginTwice_WhenCallingMe_ThenItMatchesBySubjectAndDoesNotDuplicate()
    {
        string login = NewLogin();

        using HttpClient first = CreateClient(login, "Second Timer");
        MeResponse before = await GetMeAsync(first);

        using HttpClient second = CreateClient(login, "Second Timer");
        MeResponse after = await GetMeAsync(second);

        after.UserId.Should().Be(before.UserId);

        int count = await _fixture.Factory.QueryDbAsync(database =>
            database.Users.CountAsync(user => user.ExternalSubject == login));
        count.Should().Be(1);
    }

    [Fact]
    public async Task GivenAnEditedDisplayName_WhenTheNameHeaderChanges_ThenTheEditSurvives()
    {
        string login = NewLogin();

        using HttpClient client = CreateClient(login, "Original Name");
        MeResponse created = await GetMeAsync(client);
        created.DisplayName.Should().Be("Original Name");

        // The display name is the user's to change (Feature 08 Profile), so the header must
        // never win it back on the next request.
        client.DefaultRequestHeaders.Add(AuthDefaults.CsrfHeaderName, AuthDefaults.CsrfHeaderValue);
        using HttpResponseMessage renamed = await client.PutAsJsonAsync(
            "/api/me", new UpdateMeRequest("Chosen Name"));
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpClient later = CreateClient(login, "Renamed At The Provider");
        MeResponse me = await GetMeAsync(later);

        me.UserId.Should().Be(created.UserId);
        me.DisplayName.Should().Be("Chosen Name");
    }

    [Fact]
    public async Task GivenAnRfc2047NameHeader_WhenCallingMe_ThenTheDisplayNameIsDecoded()
    {
        string login = NewLogin();
        using HttpClient client = CreateClient(login, "=?utf-8?q?Ferris_B=C3=BCller?=");

        MeResponse me = await GetMeAsync(client);

        me.DisplayName.Should().Be("Ferris Büller");
    }

    [Fact]
    public async Task GivenANameHeaderOver30Characters_WhenCallingMe_ThenTheDisplayNameIsTrimmed()
    {
        string login = NewLogin();
        using HttpClient client = CreateClient(login, "Bartholomew Montgomery Fitzwilliam the Third");

        MeResponse me = await GetMeAsync(client);

        me.DisplayName.Should().Be("Bartholomew Montgomery Fitzwil");
        me.DisplayName.Length.Should().BeLessThanOrEqualTo(User.DisplayNameMaxLength);
    }

    [Fact]
    public async Task GivenNoIdentityHeader_WhenCallingMe_ThenItIsUnauthorized()
    {
        using HttpClient client = _fixture.CookieFactory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull("an /api call is answered, never redirected");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenAnEmptyLoginHeader_WhenCallingMe_ThenItIsUnauthorized(string login)
    {
        using HttpClient client = _fixture.CookieFactory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(AuthDefaults.TailscaleLoginHeader, login);

        using HttpResponseMessage response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenAnIdentityHeaderButNoCsrfHeader_WhenMutating_ThenItIsRefused()
    {
        string login = NewLogin();
        using HttpClient client = CreateClient(login, "No Csrf Header");

        // Authenticated by the header, and still stopped by the /api group's CSRF filter.
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/me", new UpdateMeRequest("No Csrf Header"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Missing request header");
    }

    private HttpClient CreateClient(string login, string? name)
    {
        HttpClient client = _fixture.CookieFactory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(AuthDefaults.TailscaleLoginHeader, login);

        if (name is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(AuthDefaults.TailscaleNameHeader, name);
        }

        return client;
    }

    private static async Task<MeResponse> GetMeAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<MeResponse>())!;
    }

    private static string NewLogin()
    {
        // Users.Email is unique across the shared run database, and the local part doubles as
        // the fallback display name, so it has to stay inside the 30-character limit.
        string unique = Guid.CreateVersion7().ToString("N")[12..32];
        return $"ts-{unique}@example.com";
    }
}
