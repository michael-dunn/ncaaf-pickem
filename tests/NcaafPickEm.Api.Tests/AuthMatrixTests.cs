using NcaafPickEm.Api.Endpoints;
using NcaafPickEm.Api.Tests.Infrastructure;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Proves the shared <see cref="AuthMatrix"/> harness itself works, against the development-only
/// league-scoping probes.
/// </summary>
/// <remarks>
/// Every later endpoint group gets one parameterized test just like this one
/// (05-Conventions.md, Testing). P1-01 replaces the probe routes here with the real
/// <c>/api/leagues/{leagueId}</c> and <c>/api/leagues/{leagueId}/settings</c>.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class AuthMatrixTests
{
    private readonly ApiTestFixture _fixture;

    public AuthMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenLeagueScopedRoutes_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            DiagnosticsEndpoints.MemberRoute,
            DiagnosticsEndpoints.CommissionerRoute);

    [Fact]
    public async Task GivenAFormerMember_WhenCallingALeagueRoute_ThenItIs404()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        // Soft-removing a membership must take league access with it, while leaving the row
        // (and its picks) in place for history (D-008, Feature 01).
        await _fixture.Factory.ExecuteDbAsync(async database =>
        {
            Domain.Leagues.Membership membership = database.Memberships.Single(
                candidate => candidate.LeagueId == scenario.LeagueId
                    && candidate.UserId == scenario.MemberUserId);
            membership.RemovedUtc = DateTime.UtcNow;
            await database.SaveChangesAsync();
        });

        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{scenario.LeagueId}/ping");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnknownLeagueId_WhenCallingALeagueRoute_ThenItIs404NotForbidden()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{Guid.CreateVersion7()}/ping");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
