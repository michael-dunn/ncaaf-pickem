using NcaafPickEm.Api.Auth;
using NcaafPickEm.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01: the durable version of "walk every route in 03-API-Contracts.md". Reads the booted
/// app's own <c>EndpointDataSource</c> and asserts the security shape of every mapped endpoint,
/// so a new route cannot ship without an authorization scope or CSRF cover.
/// </summary>
/// <remarks>
/// The scope and CSRF facts come from the marker metadata
/// (<see cref="EndpointScopeMetadata"/>, <see cref="CsrfProtectedMetadata"/>) that
/// <c>RequireLeagueMember()</c>, <c>RequireLeagueCommissioner()</c>,
/// <c>RequireAnyLeagueCommissioner()</c> and <c>RequireCsrfHeader()</c> stamp alongside the
/// endpoint filters they add; a filter on its own leaves no trace in <c>Endpoint.Metadata</c>.
/// <see cref="GeneratedAuthMatrixTests"/> then probes the same inventory over HTTP.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class RouteInventoryTests
{
    /// <summary>
    /// The only endpoints allowed to be anonymous (P8-01 card). Everything else — including every
    /// <c>/auth</c> route that is not a sign-in entry point — must require authorization.
    /// <c>/auth/callback/google</c> is not here because it has no endpoint: it is the Google
    /// handler's <c>CallbackPath</c>, answered by the authentication middleware.
    /// </summary>
    private static readonly HashSet<string> AnonymousAllowList = new(StringComparer.Ordinal)
    {
        "health",
        "health/ready",
        "auth/login/google",

        // Development/Testing only. ProductionBehaviourTests proves it is never mapped in
        // Production, which is what makes it safe to allow-list here.
        "auth/dev-login",
    };

    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RouteInventoryTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void GivenTheMappedEndpoints_WhenInventoried_ThenEveryApiRouteIsAuthorized()
    {
        RouteFact[] routes = RouteFact.From(_fixture.Factory.Services);
        _output.WriteLine(RouteFact.Render(routes));

        routes.Should().NotBeEmpty();

        string[] offenders =
        [
            .. routes
                .Where(route => route.IsApi && (string.IsNullOrEmpty(route.Policy) || route.AllowsAnonymous))
                .Select(route => route.Describe())
        ];

        offenders.Should().BeEmpty(
            "every /api endpoint must carry an authorization policy and none may be anonymous");
    }

    [Fact]
    public void GivenTheMappedEndpoints_WhenInventoried_ThenEveryLeagueScopedRouteCarriesTheMembershipFilter()
    {
        string[] offenders =
        [
            .. RouteFact.From(_fixture.Factory.Services)
                .Where(route => route.Pattern.StartsWith("api/leagues/{leagueId", StringComparison.Ordinal))
                .Where(route => route.Scope is not (EndpointScope.LeagueMember or EndpointScope.LeagueCommissioner))
                .Select(route => route.Describe())
        ];

        offenders.Should().BeEmpty(
            "every /api/leagues/{leagueId}/** endpoint must be scoped with RequireLeagueMember() "
            + "or RequireLeagueCommissioner(), so a non-member gets 404 rather than data");
    }

    [Fact]
    public void GivenTheMappedEndpoints_WhenInventoried_ThenEveryMutationIsUnderTheCsrfGroup()
    {
        string[] offenders =
        [
            .. RouteFact.From(_fixture.Factory.Services)
                .Where(route => route.IsApi && route.IsMutation && !route.IsCsrfProtected)
                .Select(route => route.Describe())
        ];

        offenders.Should().BeEmpty("every mutating /api endpoint must be covered by CsrfEndpointFilter");
    }

    [Fact]
    public void GivenTheMappedEndpoints_WhenInventoried_ThenOnlyTheAllowListIsAnonymous()
    {
        string[] offenders =
        [
            .. RouteFact.From(_fixture.Factory.Services)
                .Where(route => route.IsApplicationRoute)
                .Where(route => string.IsNullOrEmpty(route.Policy) || route.AllowsAnonymous)
                .Where(route => !AnonymousAllowList.Contains(route.Pattern))
                .Select(route => route.Describe())
        ];

        offenders.Should().BeEmpty(
            "the only anonymous endpoints are the health probes and the sign-in entry points");
    }

    [Fact]
    public void GivenTheMappedEndpoints_WhenInventoried_ThenNothingMutatingIsServedOutsideApi()
    {
        // Static assets and the SPA fallback are the only routes outside /api, /auth and /health,
        // and they are GET/HEAD by construction. A mutation mapped outside /api would sit outside
        // the CSRF group entirely.
        string[] offenders =
        [
            .. RouteFact.From(_fixture.Factory.Services)
                .Where(route => !route.IsApi && route.IsMutation)
                .Where(route => route.Pattern is not "auth/logout")
                .Select(route => route.Describe())
        ];

        offenders.Should().BeEmpty(
            "no mutating endpoint may live outside /api; POST /auth/logout is the one documented "
            + "exception and is protected by the cookie being SameSite=Lax (03-API-Contracts.md)");
    }

    [Fact]
    public void GivenTheMappedEndpoints_WhenInventoried_ThenEveryContractRouteIsPresent()
    {
        HashSet<string> mapped = new(
            RouteFact.From(_fixture.Factory.Services).Select(route => route.Key),
            StringComparer.Ordinal);

        string[] missing = [.. ApiContractRoutes.All.Where(route => !mapped.Contains(route))];

        missing.Should().BeEmpty("every route in 03-API-Contracts.md must be mapped");
    }
}
