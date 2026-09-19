using System.Globalization;
using System.Net;
using System.Text;
using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01: the authorization matrix run against <em>every</em> mapped route rather than one route
/// per feature group. The route list comes from the app's own <c>EndpointDataSource</c>
/// (<see cref="RouteFact"/>), so a new endpoint is covered the moment it is mapped and the
/// per-group <c>*AuthMatrixTests</c> classes no longer have to be remembered.
/// </summary>
/// <remarks>
/// These probes only ever assert the <em>refusal</em> side of the matrix (401 / 404 / 403), which
/// is decided by the policy and the endpoint filters before any handler runs, so a route can be
/// probed with placeholder parameters and no per-route setup. The success side (a member or
/// commissioner actually getting 2xx) stays in the hand-written per-group matrices, which seed the
/// data each route needs.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class GeneratedAuthMatrixTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private LeagueScenario _scenario = null!;

    public GeneratedAuthMatrixTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync() =>
        _scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenAnAnonymousCaller_WhenCallingEveryApiRoute_ThenEveryOneIs401() =>
        await AssertEveryRouteAsync(
            route => route.IsApi,
            caller: null,
            HttpStatusCode.Unauthorized,
            "an anonymous /api call must be 401 JSON, never a redirect and never data");

    [Fact]
    public async Task GivenAStranger_WhenCallingEveryLeagueScopedRoute_ThenEveryOneIs404() =>
        await AssertEveryRouteAsync(
            route => route.Scope is EndpointScope.LeagueMember or EndpointScope.LeagueCommissioner,
            _scenario.StrangerUserId,
            HttpStatusCode.NotFound,
            "a non-member must never learn that a league exists (03-API-Contracts.md)");

    [Fact]
    public async Task GivenAPlainMember_WhenCallingEveryCommissionerRoute_ThenEveryOneIs403() =>
        await AssertEveryRouteAsync(
            route => route.Scope is EndpointScope.LeagueCommissioner,
            _scenario.MemberUserId,
            HttpStatusCode.Forbidden,
            "a member already knows the league exists, so a commissioner route is 403 not 404");

    [Fact]
    public async Task GivenACallerWhoCommissionsNothing_WhenCallingEveryAdminRoute_ThenEveryOneIs403() =>
        await AssertEveryRouteAsync(
            route => route.Scope is EndpointScope.AnyLeagueCommissioner,
            _scenario.MemberUserId,
            HttpStatusCode.Forbidden,
            "/api/admin/* is scoped to a commissioner of some league (D-035)");

    [Fact]
    public async Task GivenASignedInCaller_WhenMutatingEveryApiRouteWithoutTheCsrfHeader_ThenEveryOneIs400() =>
        await AssertEveryRouteAsync(
            route => route.IsApi && route.IsMutation,
            _scenario.CommissionerUserId,
            HttpStatusCode.BadRequest,
            "the CSRF filter runs ahead of the league filter, so even a commissioner is refused",
            csrfHeader: false);

    private async Task AssertEveryRouteAsync(
        Func<RouteFact, bool> selector,
        Guid? caller,
        HttpStatusCode expected,
        string because,
        bool csrfHeader = true)
    {
        RouteFact[] routes = [.. RouteFact.From(_fixture.Factory.Services).Where(selector)];

        routes.Should().NotBeEmpty("the inventory must actually select some routes to probe");
        _output.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"Probing {routes.Length} route(s) for {expected}."));

        var failures = new StringBuilder();

        foreach (RouteFact route in routes)
        {
            string path = route.ToRequestPath(_scenario.LeagueId);

            // Minimal-API model binding runs ahead of the endpoint filters that decide 404/403,
            // so a body of the wrong JSON *shape* answers 400 before authorization is reached.
            // The prober cannot know each route's request record, so a mutation is probed with
            // both an empty object and an empty array and must refuse the caller for one of them.
            HttpStatusCode[] observed = route.IsMutation
                ? [await ProbeAsync(route, path, caller, csrfHeader, "{}"),
                   await ProbeAsync(route, path, caller, csrfHeader, "[]")]
                : [await ProbeAsync(route, path, caller, csrfHeader, body: null)];

            if (!observed.Contains(expected))
            {
                failures.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"{route.PrimaryMethod} {path} -> {string.Join('/', observed.Select(status => (int)status))} "
                    + $"(expected {(int)expected})");
            }
        }

        failures.ToString().Should().BeEmpty(because);
    }

    private async Task<HttpStatusCode> ProbeAsync(
        RouteFact route,
        string path,
        Guid? caller,
        bool csrfHeader,
        string? body)
    {
        using HttpClient client = _fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(route.PrimaryMethod, path);

        if (csrfHeader)
        {
            request.Headers.Add(AuthDefaults.CsrfHeaderName, AuthDefaults.CsrfHeaderValue);
        }

        if (caller is Guid userId)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, userId.ToString());
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
