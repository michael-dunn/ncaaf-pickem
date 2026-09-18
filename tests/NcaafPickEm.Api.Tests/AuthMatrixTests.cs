using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The authorization matrix (05-Conventions.md, Testing) against the real league-scoped routes.
/// P1-01 replaced the development-only probe routes (D-021) with these.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AuthMatrixTests
{
    private readonly ApiTestFixture _fixture;

    public AuthMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheLeaguesGroup_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            "/api/leagues/{leagueId}",
            "/api/leagues/{leagueId}/settings",
            body: static () => JsonContent.Create(new UpdateLeagueSettingsRequest("Renamed League", 1, 14, 10)),
            commissionerMethod: HttpMethod.Put);

    [Fact]
    public async Task GivenTheInvitesGroup_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            "/api/leagues/{leagueId}/members", // no member-only invite route exists; members GET is the member half
            "/api/leagues/{leagueId}/invites");

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

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{scenario.LeagueId}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnknownLeagueId_WhenCallingALeagueRoute_ThenItIs404NotForbidden()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
