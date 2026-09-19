using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Contracts.Push;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>/api/push/*</c>: the subscription upsert, the idempotent delete, the per-device status, the
/// VAPID key endpoint and the commissioner's notification log (Feature 11, P7-01).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class PushSubscriptionTests
{
    private readonly ApiTestFixture _fixture;

    public PushSubscriptionTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheSameEndpointTwice_WhenSubscribing_ThenThereIsOneRow()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string endpoint = NewEndpoint();

        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage first = await SubscribeAsync(client, endpoint, "keyA", "authA", "Phone");
        using HttpResponseMessage second = await SubscribeAsync(client, endpoint, "keyB", "authB", "Phone (again)");

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await _fixture.Factory.QueryDbAsync(database => database.PushSubscriptions
            .Where(subscription => subscription.Endpoint == endpoint)
            .ToListAsync());

        rows.Should().ContainSingle();
        rows[0].UserId.Should().Be(user.Id);
        rows[0].P256dh.Should().Be("keyB", "the upsert refreshes the key material the browser handed us");
        rows[0].Auth.Should().Be("authB");
        rows[0].UserAgent.Should().Be("Phone (again)");
    }

    [Fact]
    public async Task GivenAnEndpointOwnedByAnotherUser_WhenSubscribing_ThenItIsReOwned()
    {
        User first = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        User second = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string endpoint = NewEndpoint();

        using HttpClient firstClient = _fixture.Factory.CreateMutatingClientAs(first.Id);
        using HttpResponseMessage firstResponse = await SubscribeAsync(firstClient, endpoint, "k1", "a1", null);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A shared device signs out and signs back in as somebody else: the endpoint follows the
        // person who is actually using it.
        using HttpClient secondClient = _fixture.Factory.CreateMutatingClientAs(second.Id);
        using HttpResponseMessage secondResponse = await SubscribeAsync(secondClient, endpoint, "k2", "a2", null);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await _fixture.Factory.QueryDbAsync(database => database.PushSubscriptions
            .Where(subscription => subscription.Endpoint == endpoint)
            .ToListAsync());

        rows.Should().ContainSingle();
        rows[0].UserId.Should().Be(second.Id);
    }

    [Fact]
    public async Task GivenASubscription_WhenDeletingTwice_ThenBothAnswer204AndTheRowIsGone()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string endpoint = NewEndpoint();

        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);
        using HttpResponseMessage subscribe = await SubscribeAsync(client, endpoint, "k", "a", null);
        subscribe.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using HttpResponseMessage firstDelete = await DeleteAsync(client, endpoint);
        using HttpResponseMessage secondDelete = await DeleteAsync(client, endpoint);

        firstDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        bool exists = await _fixture.Factory.QueryDbAsync(database =>
            database.PushSubscriptions.AnyAsync(subscription => subscription.Endpoint == endpoint));

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAnEndpointOwnedByAnotherUser_WhenDeleting_ThenItIs204AndTheRowSurvives()
    {
        User owner = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        User other = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string endpoint = NewEndpoint();

        using HttpClient ownerClient = _fixture.Factory.CreateMutatingClientAs(owner.Id);
        using HttpResponseMessage subscribe = await SubscribeAsync(ownerClient, endpoint, "k", "a", null);
        subscribe.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using HttpClient otherClient = _fixture.Factory.CreateMutatingClientAs(other.Id);
        using HttpResponseMessage delete = await DeleteAsync(otherClient, endpoint);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        bool exists = await _fixture.Factory.QueryDbAsync(database =>
            database.PushSubscriptions.AnyAsync(subscription => subscription.Endpoint == endpoint));

        exists.Should().BeTrue("one account cannot unsubscribe another account's device");
    }

    [Fact]
    public async Task GivenASubscription_WhenAskingStatus_ThenItIsTrueForThatDeviceOnly()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string subscribed = NewEndpoint();
        string unknown = NewEndpoint();

        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);
        using HttpResponseMessage subscribe = await SubscribeAsync(client, subscribed, "k", "a", null);
        subscribe.StatusCode.Should().Be(HttpStatusCode.NoContent);

        PushStatusResponse? on = await client.GetFromJsonAsync<PushStatusResponse>(StatusRoute(subscribed));
        PushStatusResponse? off = await client.GetFromJsonAsync<PushStatusResponse>(StatusRoute(unknown));
        PushStatusResponse? missing = await client.GetFromJsonAsync<PushStatusResponse>("/api/push/status");

        on.Should().NotBeNull();
        on!.HasSubscriptionForThisDevice.Should().BeTrue();
        off.Should().NotBeNull();
        off!.HasSubscriptionForThisDevice.Should().BeFalse();
        missing.Should().NotBeNull();
        missing!.HasSubscriptionForThisDevice.Should().BeFalse("no endpoint means no device to answer for");
    }

    [Fact]
    public async Task GivenAnotherUsersDevice_WhenAskingStatus_ThenItIsFalse()
    {
        User owner = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        User other = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string endpoint = NewEndpoint();

        using HttpClient ownerClient = _fixture.Factory.CreateMutatingClientAs(owner.Id);
        using HttpResponseMessage subscribe = await SubscribeAsync(ownerClient, endpoint, "k", "a", null);
        subscribe.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using HttpClient otherClient = _fixture.Factory.CreateClientAs(other.Id);
        PushStatusResponse? status = await otherClient.GetFromJsonAsync<PushStatusResponse>(StatusRoute(endpoint));

        status.Should().NotBeNull();
        status!.HasSubscriptionForThisDevice.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "k", "a")]
    [InlineData("not-a-url", "k", "a")]
    [InlineData("http://insecure.example/push/1", "k", "a")]
    [InlineData("https://push.example/1", "", "a")]
    [InlineData("https://push.example/1", "k", "")]
    public async Task GivenAMalformedSubscription_WhenSubscribing_ThenItIs400(
        string endpoint,
        string p256dh,
        string auth)
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage response = await SubscribeAsync(client, endpoint, p256dh, auth, null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnEndpointOverTheColumnLength_WhenSubscribing_ThenItIs400()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        string tooLong = "https://push.example/" + new string('x', 2100);

        using HttpResponseMessage response = await SubscribeAsync(client, tooLong, "k", "a", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("GET", "/api/push/vapid-public-key")]
    [InlineData("GET", "/api/push/status?endpoint=https%3A%2F%2Fpush.example%2F1")]
    [InlineData("POST", "/api/push/subscriptions")]
    [InlineData("DELETE", "/api/push/subscriptions")]
    public async Task GivenAnAnonymousCaller_WhenCallingPush_ThenItIs401(string method, string route)
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add(AuthDefaults.CsrfHeaderName, AuthDefaults.CsrfHeaderValue);

        if (!string.Equals(method, "GET", StringComparison.Ordinal))
        {
            request.Content = JsonContent.Create(
                new PushSubscriptionRequest("https://push.example/1", "k", "a", null));
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenTheNotificationLog_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            "/api/leagues/{leagueId}/members", // no member-only notification route exists
            "/api/leagues/{leagueId}/notifications/log");

    [Fact]
    public async Task GivenNoVapidKeysConfigured_WhenAskingForThePublicKey_ThenItIs503()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateClientAs(user.Id);

        // The test host sets no Push__* keys, which is exactly the "app boots without push"
        // configuration the card requires.
        using HttpResponseMessage response = await client.GetAsync("/api/push/vapid-public-key");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not configured");
    }

    [Fact]
    public async Task GivenAStoredSubscription_WhenComparingHashes_ThenTheComputedColumnMatchesTheClient()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        string endpoint = NewEndpoint();

        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);
        using HttpResponseMessage subscribe = await SubscribeAsync(client, endpoint, "k", "a", null);
        subscribe.StatusCode.Should().Be(HttpStatusCode.NoContent);

        byte[] stored = await _fixture.Factory.QueryDbAsync(database => database.PushSubscriptions
            .Where(subscription => subscription.Endpoint == endpoint)
            .Select(subscription => subscription.EndpointHash)
            .SingleAsync());

        // If these ever diverge every lookup in PushEndpoints silently misses and the upsert
        // starts duplicating rows, so it is asserted against a real SQL Server (D-017).
        stored.Should().Equal(PushEndpointHash.Compute(endpoint));
    }

    private static string NewEndpoint() => $"https://push.example/send/{Guid.CreateVersion7():N}";

    private static string StatusRoute(string endpoint) =>
        $"/api/push/status?endpoint={Uri.EscapeDataString(endpoint)}";

    private static Task<HttpResponseMessage> SubscribeAsync(
        HttpClient client,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent) =>
        client.PostAsJsonAsync(
            "/api/push/subscriptions",
            new PushSubscriptionRequest(endpoint, p256dh, auth, userAgent));

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions")
        {
            Content = JsonContent.Create(new DeletePushSubscriptionRequest(endpoint)),
        };

        return await client.SendAsync(request);
    }
}
