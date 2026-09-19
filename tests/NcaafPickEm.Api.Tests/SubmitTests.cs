using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>POST /api/leagues/{leagueId}/weeks/{week}/picks/me/submit</c> (Feature 04, P4-01). Runs on
/// its own <see cref="ClockedApp"/>: "a game added after you submitted" only means anything if
/// time can pass between the two.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class SubmitTests : IAsyncLifetime
{
    private readonly ClockedApp _app;

    public SubmitTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GivenUnpickedGames_WhenSubmitting_ThenItIs409WithTheMissingCount()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAsync(client, scenario, scenario.Games[0]);

        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("IncompletePicks");
        body.Should().Contain("\"count\":" + (scenario.Games.Length - 1));
    }

    [Fact]
    public async Task GivenEveryGamePicked_WhenSubmitting_ThenTheMemberIsSubmitted()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAllAsync(client, scenario);

        _app.Advance(TimeSpan.FromMinutes(1));
        DateTime submitInstant = _app.NowUtc.UtcDateTime;

        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.Status.Should().Be(SubmissionStatus.Submitted);
        body.PickedCount.Should().Be(scenario.Games.Length);
        body.TotalCount.Should().Be(scenario.Games.Length);

        WeekSubmission submission = await ReadSubmissionAsync(scenario);
        submission.Status.Should().Be(SubmissionStatus.Submitted);
        submission.SubmittedUtc.Should().Be(submitInstant);
    }

    [Fact]
    public async Task GivenASubmittedMember_WhenTheyChangeAPick_ThenTheyStaySubmitted()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAllAsync(client, scenario);
        await SubmitAsync(client, scenario);
        WeekSubmission before = await ReadSubmissionAsync(scenario);

        _app.Advance(TimeSpan.FromMinutes(5));
        GameSetGameDto game = scenario.Games[0];
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.AwayTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.Status.Should().Be(SubmissionStatus.Submitted);
        body.Games.Single(row => row.Game.GameId == game.GameId).MyTeamId.Should().Be(game.AwayTeam.TeamId);

        WeekSubmission after = await ReadSubmissionAsync(scenario);
        after.SubmittedUtc.Should().Be(before.SubmittedUtc);
    }

    [Fact]
    public async Task GivenASubmittedMember_WhenTheySubmitAgain_ThenNothingChanges()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAllAsync(client, scenario);
        await SubmitAsync(client, scenario);
        WeekSubmission first = await ReadSubmissionAsync(scenario);

        _app.Advance(TimeSpan.FromMinutes(5));
        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.Status.Should().Be(SubmissionStatus.Submitted);

        WeekSubmission second = await ReadSubmissionAsync(scenario);
        second.SubmittedUtc.Should().Be(first.SubmittedUtc);
        second.LastChangedUtc.Should().Be(first.LastChangedUtc);
    }

    [Fact]
    public async Task GivenASubmittedMember_WhenAGameIsAddedToTheSet_ThenTheyRevertToInProgressAndSeeItAsNew()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAllAsync(client, scenario);
        await SubmitAsync(client, scenario);

        _app.Advance(TimeSpan.FromHours(1));

        // Ohio State / Wisconsin: eligible, but never selected by the Top 25 rule.
        Guid extra = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005);
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(extra)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using HttpResponseMessage response = await client.GetAsync($"{scenario.PicksRoute}/me");

        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.Status.Should().Be(SubmissionStatus.InProgress);
        body.TotalCount.Should().Be(scenario.Games.Length + 1);
        body.PickedCount.Should().Be(scenario.Games.Length);
        body.Games.Should().ContainSingle(row => row.Game.GameId == extra && row.IsNewSinceSubmit);

        // Picking the new game and submitting again clears both the status and the "new" ribbon.
        _app.Advance(TimeSpan.FromMinutes(1));
        await PickAsync(client, scenario, body.Games.Single(row => row.Game.GameId == extra).Game);
        await SubmitAsync(client, scenario);

        using HttpResponseMessage after = await client.GetAsync($"{scenario.PicksRoute}/me");
        MyPicksResponse? afterBody = await after.Content.ReadFromJsonAsync<MyPicksResponse>();
        afterBody!.Status.Should().Be(SubmissionStatus.Submitted);
        afterBody.Games.Should().OnlyContain(row => !row.IsNewSinceSubmit);
    }

    [Fact]
    public async Task GivenASubmittedMember_WhenAGameIsRemovedFromTheSet_ThenTheyStaySubmitted()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAllAsync(client, scenario);
        await SubmitAsync(client, scenario);

        _app.Advance(TimeSpan.FromMinutes(5));
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage remove = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games/{scenario.Games[0].GameId}"))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using HttpResponseMessage response = await client.GetAsync($"{scenario.PicksRoute}/me");
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();

        body!.Status.Should().Be(SubmissionStatus.Submitted);
        body.TotalCount.Should().Be(scenario.Games.Length - 1);
        body.PickedCount.Should().Be(scenario.Games.Length - 1);
        body.Games.Should().NotContain(row => row.Game.GameId == scenario.Games[0].GameId);
    }

    [Fact]
    public async Task GivenAWeekThatIsNotCurrent_WhenSubmitting_ThenItIs409()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/6/picks/me/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WeekNotCurrent");
    }

    private async Task<(PickWeekScenario Scenario, HttpClient Client)> CreateAsync()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        return (scenario, _app.Factory.CreateMutatingClientAs(scenario.MemberUserId));
    }

    private async Task<WeekSubmission> ReadSubmissionAsync(PickWeekScenario scenario) =>
        await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .SingleAsync(submission => submission.WeekGameSetId == scenario.WeekGameSetId
                && submission.MembershipId == scenario.MemberMembershipId));

    private static async Task PickAllAsync(HttpClient client, PickWeekScenario scenario)
    {
        foreach (GameSetGameDto game in scenario.Games)
        {
            await PickAsync(client, scenario, game);
        }
    }

    private static async Task PickAsync(HttpClient client, PickWeekScenario scenario, GameSetGameDto game)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SubmitAsync(HttpClient client, PickWeekScenario scenario)
    {
        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
