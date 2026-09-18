using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET</c> and <c>PUT /api/me</c> (Feature 08 Profile).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class MeEndpointTests
{
    private readonly ApiTestFixture _fixture;

    public MeEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenSignedInUser_WhenGettingMe_ThenItReturnsTheAccount()
    {
        User user = await _fixture.Factory.QueryDbAsync(database =>
            TestUsers.CreateUserAsync(database, "Reads Me"));

        using HttpClient client = _fixture.Factory.CreateClientAs(user.Id);

        MeResponse? me = await client.GetFromJsonAsync<MeResponse>("/api/me");

        me.Should().NotBeNull();
        me!.UserId.Should().Be(user.Id);
        me.DisplayName.Should().Be("Reads Me");
        me.Email.Should().Be(user.Email);

        // P1-01 fills this in; until then an empty array is the contract.
        me.Leagues.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenSignedInUser_WhenSettingDisplayName_ThenItIsStored()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage response =
            await client.PutAsJsonAsync("/api/me", new UpdateMeRequest("  Renamed  "));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        MeResponse? body = await response.Content.ReadFromJsonAsync<MeResponse>();
        body!.DisplayName.Should().Be("Renamed");

        string stored = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id)).DisplayName);
        stored.Should().Be("Renamed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This display name is far too long to fit")]
    public async Task GivenAnOutOfRangeDisplayName_WhenSettingIt_ThenItIsRejected(string displayName)
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Unchanged"));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage response =
            await client.PutAsJsonAsync("/api/me", new UpdateMeRequest(displayName));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string stored = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id)).DisplayName);
        stored.Should().Be("Unchanged");
    }

    [Fact]
    public async Task GivenAnonymousCaller_WhenSettingDisplayName_ThenItIsUnauthorized()
    {
        using HttpClient client = _fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me")
        {
            Content = JsonContent.Create(new UpdateMeRequest("Nope")),
        };
        request.Headers.Add(Auth.AuthDefaults.CsrfHeaderName, Auth.AuthDefaults.CsrfHeaderValue);

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
