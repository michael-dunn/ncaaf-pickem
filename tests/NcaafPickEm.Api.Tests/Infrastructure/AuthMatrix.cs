using System.Net;
using NcaafPickEm.Api.Auth;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Runs the four-case authorization matrix `05-Conventions.md` makes mandatory for every
/// league-scoped endpoint group: anonymous -> 401, non-member -> 404, member on a commissioner
/// route -> 403, commissioner -> 2xx.
/// </summary>
/// <remarks>
/// Call it once per endpoint group from a single parameterized test. Route templates use
/// <c>{leagueId}</c>; the harness seeds its own league and callers, so groups stay independent.
/// </remarks>
public static class AuthMatrix
{
    /// <summary>The placeholder a route template uses for the seeded league's id.</summary>
    public const string LeagueIdPlaceholder = "{leagueId}";

    /// <summary>
    /// Exercises the matrix against one member-scoped route and one commissioner-scoped route.
    /// </summary>
    /// <param name="fixture">The shared fixture.</param>
    /// <param name="method">HTTP method both routes answer.</param>
    /// <param name="memberRoute">Route template a member may call, containing <c>{leagueId}</c>.</param>
    /// <param name="commissionerRoute">
    /// Route template only a commissioner may call, containing <c>{leagueId}</c>. Null skips the
    /// commissioner half for groups that have no commissioner-only route.
    /// </param>
    /// <param name="body">Request body factory for a mutating method.</param>
    public static async Task RunAsync(
        ApiTestFixture fixture,
        HttpMethod method,
        string memberRoute,
        string? commissionerRoute = null,
        Func<HttpContent>? body = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberRoute);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(fixture.Factory);

        string member = memberRoute.Replace(
            LeagueIdPlaceholder, scenario.LeagueId.ToString(), StringComparison.Ordinal);

        await AssertStatusAsync(fixture, method, member, caller: null, HttpStatusCode.Unauthorized, body);
        await AssertStatusAsync(fixture, method, member, scenario.StrangerUserId, HttpStatusCode.NotFound, body);
        await AssertSuccessAsync(fixture, method, member, scenario.MemberUserId, body);

        if (commissionerRoute is null)
        {
            return;
        }

        string commish = commissionerRoute.Replace(
            LeagueIdPlaceholder, scenario.LeagueId.ToString(), StringComparison.Ordinal);

        await AssertStatusAsync(fixture, method, commish, caller: null, HttpStatusCode.Unauthorized, body);
        await AssertStatusAsync(fixture, method, commish, scenario.StrangerUserId, HttpStatusCode.NotFound, body);
        await AssertStatusAsync(fixture, method, commish, scenario.MemberUserId, HttpStatusCode.Forbidden, body);
        await AssertSuccessAsync(fixture, method, commish, scenario.CommissionerUserId, body);
    }

    private static async Task AssertStatusAsync(
        ApiTestFixture fixture,
        HttpMethod method,
        string route,
        Guid? caller,
        HttpStatusCode expected,
        Func<HttpContent>? body)
    {
        using HttpResponseMessage response = await SendAsync(fixture, method, route, caller, body);

        response.StatusCode.Should().Be(
            expected,
            "{0} {1} as {2} must be {3}",
            method,
            route,
            caller?.ToString() ?? "anonymous",
            expected);
    }

    private static async Task AssertSuccessAsync(
        ApiTestFixture fixture,
        HttpMethod method,
        string route,
        Guid caller,
        Func<HttpContent>? body)
    {
        using HttpResponseMessage response = await SendAsync(fixture, method, route, caller, body);

        response.IsSuccessStatusCode.Should().BeTrue(
            "{0} {1} as an authorized caller must succeed, but was {2}",
            method,
            route,
            response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        ApiTestFixture fixture,
        HttpMethod method,
        string route,
        Guid? caller,
        Func<HttpContent>? body)
    {
        using HttpClient client = fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(method, route);

        // Always present so a missing CSRF header can never be mistaken for an auth failure.
        request.Headers.Add(AuthDefaults.CsrfHeaderName, AuthDefaults.CsrfHeaderValue);

        if (caller is Guid userId)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, userId.ToString());
        }

        if (body is not null)
        {
            request.Content = body();
        }

        return await client.SendAsync(request);
    }
}
