using System.Net;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Users;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The authorization matrix (05-Conventions.md, Testing) for the leaderboard endpoint group added
/// by P5-03: anonymous 401, non-member 404, member 2xx. The group has no commissioner-only route.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LeaderboardAuthMatrixTests
{
    private readonly ApiTestFixture _fixture;

    public LeaderboardAuthMatrixTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("/api/leagues/{leagueId}/leaderboard")]
    [InlineData("/api/leagues/{leagueId}/weeks/7/leaderboard")]
    public async Task GivenALeaderboardRoute_WhenCalledByEachRole_ThenTheMatrixHolds(string route) =>
        await AuthMatrix.RunAsync(_fixture, HttpMethod.Get, route);

    [Fact]
    public async Task GivenTheGridRoute_WhenCalledByEachRole_ThenTheMatrixHolds()
    {
        // The grid answers 403 to a member until the week locks, so it cannot go through
        // AuthMatrix.RunAsync (which seeds a bare league and expects the member to succeed).
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        User stranger = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateUserAsync(db));
        string route = $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/grid";

        using (HttpClient anonymous = _fixture.PinnedFactory.CreateClient())
        using (HttpResponseMessage response = await anonymous.GetAsync(route))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (HttpClient outsider = _fixture.PinnedFactory.CreateClientAs(stranger.Id))
        using (HttpResponseMessage response = await outsider.GetAsync(route))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member cannot tell the league exists");
        }

        using (HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId))
        using (HttpResponseMessage response = await member.GetAsync(route))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
