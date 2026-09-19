using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>PUT /api/leagues/{leagueId}/weeks/{week}/picks/me/{gameId}</c> (Feature 04, P4-01).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class SetPickTests
{
    private readonly ApiTestFixture _fixture;

    public SetPickTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenACurrentWeekGame_WhenAMemberPicksATeam_ThenItIsSavedAndTheyAreInProgress()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();

        body!.Status.Should().Be(SubmissionStatus.InProgress);
        body.PickedCount.Should().Be(1);
        body.TotalCount.Should().Be(scenario.Games.Length);
        body.IsLocked.Should().BeFalse();
        body.LockAtUtc.Should().NotBeNull();
        body.LockAtEasternDisplay.Should().NotBeNullOrWhiteSpace();
        body.Games.Should().ContainSingle(row => row.Game.GameId == game.GameId && row.MyTeamId == game.HomeTeam.TeamId);
        body.Games.Should().OnlyContain(row => !row.IsNewSinceSubmit);
    }

    [Fact]
    public async Task GivenAPickedGame_WhenTheMemberTapsTheOtherTeam_ThenThePickMoves()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        await PickAsync(client, scenario, game, game.HomeTeam.TeamId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.AwayTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.PickedCount.Should().Be(1);
        body.Games.Single(row => row.Game.GameId == game.GameId).MyTeamId.Should().Be(game.AwayTeam.TeamId);
    }

    [Fact]
    public async Task GivenAPickedGame_WhenTheMemberTapsTheSameTeamAgain_ThenItIsANoOpSuccess()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        await PickAsync(client, scenario, game, game.HomeTeam.TeamId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.PickedCount.Should().Be(1);
        body.Games.Single(row => row.Game.GameId == game.GameId).MyTeamId.Should().Be(game.HomeTeam.TeamId);
    }

    [Fact]
    public async Task GivenATeamThatIsNotInTheGame_WhenPicking_ThenItIs400()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];
        GameSetGameDto other = scenario.Games[1];

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(other.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertTitleAsync(response, "TeamNotInGame");
    }

    [Fact]
    public async Task GivenAnEmptyTeamId_WhenPicking_ThenItIsAValidationProblem()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{scenario.Games[0].GameId}", new SetPickRequest(Guid.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAGameRemovedFromTheSet_WhenPicking_ThenItIs409()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        using HttpClient commish = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{game.GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertTitleAsync(response, "GameNotActive");
    }

    [Fact]
    public async Task GivenAGameThatIsNotInTheWeeksSet_WhenPicking_ThenItIs404()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();

        // Ohio State / Wisconsin is a real fixture game the Top 25 rule never selects.
        Guid unselected = await FixtureGameData.GetGameIdAsync(_fixture.PinnedFactory, 700005);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{unselected}", new SetPickRequest(scenario.Games[0].HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertTitleAsync(response, "GameNotInSet");
    }

    [Fact]
    public async Task GivenAPastWeek_WhenPicking_ThenItIs409()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/6/picks/me/{game.GameId}",
            new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertTitleAsync(response, "WeekNotCurrent");
    }

    [Fact]
    public async Task GivenAFutureWeek_WhenPicking_ThenItIs409()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/8/picks/me/{game.GameId}",
            new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertTitleAsync(response, "WeekNotCurrent");
    }

    [Fact]
    public async Task GivenAWeekOutsideTheLeaguesRange_WhenPicking_ThenItIs404()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/99/picks/me/{game.GameId}",
            new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertTitleAsync(response, "WeekOutOfRange");
    }

    [Fact]
    public async Task GivenAPastWeekWithAGeneratedSet_WhenReadingMyPicks_ThenItIsReadableAndEmpty()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/6/picks/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.Week.Should().Be(6);
        body.Status.Should().Be(SubmissionStatus.NotStarted);
        body.TotalCount.Should().Be(0);
        body.Games.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenPicksBySeveralMembers_WhenEachReadsTheirOwn_ThenTheyOnlySeeTheirOwn()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        await PickAsync(client, scenario, game, game.HomeTeam.TeamId);

        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);
        using HttpResponseMessage response = await second.GetAsync($"{scenario.PicksRoute}/me");

        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.PickedCount.Should().Be(0);
        body.Games.Should().OnlyContain(row => row.MyTeamId == null);
    }

    private async Task<(PickWeekScenario Scenario, HttpClient Client)> CreateAsync()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        return (scenario, _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId));
    }

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

    private static async Task AssertTitleAsync(HttpResponseMessage response, string expected)
    {
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(expected);
    }
}
