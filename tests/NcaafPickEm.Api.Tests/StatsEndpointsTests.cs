using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Stats;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The member stats endpoints (Feature 14): the authorization matrix and what the queries make of
/// a locked, partly decided week. Games are decided by result overrides rather than scores on the
/// shared fixture <c>Games</c> rows, so no other league's copy of a game changes.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class StatsEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public StatsEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheLeagueStatsRoute_WhenCalledByEachRole_ThenTheMatrixHolds() =>
        await AuthMatrix.RunAsync(_fixture, HttpMethod.Get, "/api/leagues/{leagueId}/stats");

    [Fact]
    public async Task GivenTheMemberStatsRoute_WhenCalledByEachRole_ThenTheMatrixHolds()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        User stranger = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateUserAsync(db));
        string route = ProfileRoute(scenario, scenario.SecondMemberMembershipId);

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
            response.StatusCode.Should().Be(HttpStatusCode.OK, "any member may open any member's profile");
        }
    }

    [Fact]
    public async Task GivenAMembershipFromAnotherLeague_WhenOpeningItsProfile_ThenItIs404()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        PickWeekScenario other = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(ProfileRoute(scenario, other.MemberMembershipId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MembershipNotFound");
    }

    [Fact]
    public async Task GivenNothingLocked_WhenReadingLeagueStats_ThenThroughWeekIsNull()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        LeagueStats body = await GetAsync<LeagueStats>(
            scenario.MemberUserId, $"/api/leagues/{scenario.LeagueId}/stats");

        body.ThroughWeek.Should().BeNull("the page shows its empty state");
        body.Accuracy.Should().HaveCount(3);
    }

    [Fact]
    public async Task GivenALockedPartlyDecidedWeek_WhenReadingStats_ThenAccuracyAndUnderdogHitsCountOnlyDecidedGames()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient first = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);

        GameSetGameDto upset = scenario.Games[0];
        GameSetGameDto chalk = scenario.Games[1];
        GameSetGameDto voided = scenario.Games[2];

        // Upset: home favored by 7, away wins. Chalk: no line, home wins.
        await PickAsync(first, scenario, upset, upset.AwayTeam.TeamId);
        await PickAsync(second, scenario, upset, upset.HomeTeam.TeamId);
        await PickAsync(first, scenario, chalk, chalk.HomeTeam.TeamId);
        await PickAsync(first, scenario, voided, voided.HomeTeam.TeamId);

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        Guid[] decidedIds = [upset.GameSetGameId!.Value, chalk.GameSetGameId!.Value];
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            List<WeekGameSetGame> rows = await db.WeekGameSetGames
                .Where(row => row.WeekGameSetId == scenario.WeekGameSetId && !row.IsRemoved)
                .ToListAsync();

            foreach (WeekGameSetGame row in rows)
            {
                // Other suites advance the shared fixture scores, so the rest of the set may or may
                // not be Final; voiding it keeps the two override rows the only counted games.
                if (!decidedIds.Contains(row.Id))
                {
                    row.IsVoided = true;
                }
            }

            WeekGameSetGame upsetRow = rows.Single(row => row.Id == upset.GameSetGameId!.Value);
            upsetRow.SpreadAtLock = -7m;
            upsetRow.ResultOverrideWinnerTeamId = upset.AwayTeam.TeamId;

            WeekGameSetGame chalkRow = rows.Single(row => row.Id == chalk.GameSetGameId!.Value);
            chalkRow.SpreadAtLock = null;
            chalkRow.ResultOverrideWinnerTeamId = chalk.HomeTeam.TeamId;

            WeekGameSet set = await db.WeekGameSets.SingleAsync(candidate => candidate.Id == scenario.WeekGameSetId);
            set.IsComplete = true;

            db.WeekResults.Add(Result(scenario.WeekGameSetId, scenario.MemberMembershipId, points: 20, correct: 2));
            db.WeekResults.Add(Result(scenario.WeekGameSetId, scenario.SecondMemberMembershipId, points: 0, correct: 0));

            await db.SaveChangesAsync();
        });

        LeagueStats league = await GetAsync<LeagueStats>(
            scenario.SecondMemberUserId, $"/api/leagues/{scenario.LeagueId}/stats");

        league.ThroughWeek.Should().Be(PickWeekScenario.Week);
        league.IsProvisional.Should().BeFalse();
        league.Accuracy.Should().HaveCount(3, "every active member is listed, the commissioner included");

        StatRow mine = league.Accuracy.Single(row => row.MembershipId == scenario.MemberMembershipId);
        mine.Should().Match<StatRow>(row => row.Rank == 1 && row.Hits == 2 && row.Total == 2 && !row.IsMe);

        StatRow theirs = league.Accuracy.Single(row => row.MembershipId == scenario.SecondMemberMembershipId);
        theirs.Should().Match<StatRow>(row => row.Hits == 0 && row.Total == 2 && row.IsMe,
            "the unpicked chalk game is a miss, and the voided game is not counted");

        league.Underdog.Single(row => row.MembershipId == scenario.MemberMembershipId)
            .Should().Match<StatRow>(row => row.Hits == 1 && row.Total == 1, "only the upset had a line and an underdog win");

        MemberStatsProfile profile = await GetAsync<MemberStatsProfile>(
            scenario.MemberUserId, ProfileRoute(scenario, scenario.MemberMembershipId));

        profile.IsMe.Should().BeTrue();
        profile.Accuracy.Should().Be(new MemberStat(1, 3, 2, 2));
        profile.Underdog.Should().Be(new MemberStat(1, 3, 1, 1));
        profile.MostPoints!.Week.Should().Be(PickWeekScenario.Week);
        profile.MostPoints.Value.Should().Be(20);
        profile.Weeks.Should().ContainSingle().Which.Rank.Should().Be(1);
    }

    private static string ProfileRoute(PickWeekScenario scenario, Guid membershipId) =>
        $"/api/leagues/{scenario.LeagueId}/members/{membershipId}/stats";

    private static WeekResult Result(Guid weekGameSetId, Guid membershipId, int points, int correct) => new()
    {
        MembershipId = membershipId,
        WeekGameSetId = weekGameSetId,
        Points = points,
        CorrectCount = correct,
        ActiveGameCount = 2,
        IsWeekComplete = true,
        ComputedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
    };

    private static async Task PickAsync(
        HttpClient client,
        PickWeekScenario scenario,
        GameSetGameDto game,
        Guid teamId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(teamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<T> GetAsync<T>(Guid callerUserId, string route)
    {
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(callerUserId);
        using HttpResponseMessage response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
