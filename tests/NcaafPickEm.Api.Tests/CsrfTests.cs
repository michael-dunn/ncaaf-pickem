using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Auth;
using NcaafPickEm.Shared.Contracts.Push;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The <c>X-Requested-With: NcaafPickEm</c> rule on mutating <c>/api</c> calls
/// (01-Architecture.md, CSRF).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class CsrfTests
{
    private readonly ApiTestFixture _fixture;

    public CsrfTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAMutationWithoutTheHeader_WhenCalled_ThenItIsBadRequest()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Before"));

        // CreateClientAs deliberately does not add the CSRF header; only the mutating client does.
        using HttpClient client = _fixture.Factory.CreateClientAs(user.Id);

        using HttpResponseMessage response =
            await client.PutAsJsonAsync("/api/me", new UpdateMeRequest("After"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAMutationWithTheWrongHeaderValue_WhenCalled_ThenItIsBadRequest()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateClientAs(user.Id);
        client.DefaultRequestHeaders.Add(AuthDefaults.CsrfHeaderName, "XMLHttpRequest");

        using HttpResponseMessage response =
            await client.PutAsJsonAsync("/api/me", new UpdateMeRequest("After"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAReadWithoutTheHeader_WhenCalled_ThenItSucceeds()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateClientAs(user.Id);

        using HttpResponseMessage response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// P8-01: the filter keys on the HTTP method, not on whether a body was sent, so a
    /// <c>DELETE</c> that carries one is covered too.
    /// </summary>
    [Fact]
    public async Task GivenADeleteWithABody_WhenSentWithoutTheHeader_ThenItIsBadRequest()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateClientAs(user.Id);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions")
        {
            Content = JsonContent.Create(
                new DeletePushSubscriptionRequest("https://push.example.com/endpoint/csrf")),
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// P8-01: <c>/api/admin/*</c> is scoped by its own filter rather than by <c>{leagueId}</c>,
    /// so it is worth proving the group-level CSRF filter still reaches it.
    /// </summary>
    [Fact]
    public async Task GivenAnAdminMutation_WhenSentWithoutTheHeader_ThenItIsBadRequest()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response =
            await client.PostAsync("/api/admin/refresh/Teams", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenHealthAtTheRoot_WhenProbedWithoutTheHeader_ThenItIsNotSubjectToTheRule()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
