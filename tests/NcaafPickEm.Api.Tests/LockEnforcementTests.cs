using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Server-side lock enforcement (Feature 04, Lock behavior): picks stop at <c>LockAtUtc</c>
/// whether or not P4-02's lock job has run yet.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LockEnforcementTests : IAsyncLifetime
{
    private readonly ClockedApp _app;

    public LockEnforcementTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public async Task GivenTheLockInstantHasArrived_WhenPicking_ThenItIs409EvenThoughTheJobHasNotRun(int minutesPastLock)
    {
        (PickWeekScenario scenario, HttpClient client, DateTimeOffset lockAtUtc) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        _app.Set(lockAtUtc.AddMinutes(minutesPastLock));

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");

        // The job really has not run: nothing has written LockedUtc.
        DateTime? lockedUtc = await _app.Factory.QueryDbAsync(db => db.WeekGameSets
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.LockedUtc)
            .SingleAsync());
        lockedUtc.Should().BeNull();
    }

    [Fact]
    public async Task GivenTheLockInstantHasArrived_WhenSubmitting_ThenItIs409()
    {
        (PickWeekScenario scenario, HttpClient client, DateTimeOffset lockAtUtc) = await CreateAsync();
        foreach (GameSetGameDto game in scenario.Games)
        {
            using HttpResponseMessage pick = await client.PutAsJsonAsync(
                $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
            pick.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _app.Set(lockAtUtc);

        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");
    }

    [Fact]
    public async Task GivenOneSecondBeforeLock_WhenPicking_ThenItStillSucceeds()
    {
        (PickWeekScenario scenario, HttpClient client, DateTimeOffset lockAtUtc) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        _app.Set(lockAtUtc.AddSeconds(-1));

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GivenTheLockInstantHasArrived_WhenReadingMyPicks_ThenItIsReadOnlyButStillReadable()
    {
        (PickWeekScenario scenario, HttpClient client, DateTimeOffset lockAtUtc) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        using (HttpResponseMessage pick = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId)))
        {
            pick.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _app.Set(lockAtUtc);

        using HttpResponseMessage response = await client.GetAsync($"{scenario.PicksRoute}/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.IsLocked.Should().BeTrue();
        body.PickedCount.Should().Be(1);
        body.Games.Single(row => row.Game.GameId == game.GameId).MyTeamId.Should().Be(game.HomeTeam.TeamId);
    }

    [Fact]
    public async Task GivenTheLockJobHasRun_WhenPickingBeforeTheLockInstant_ThenItIsStill409()
    {
        (PickWeekScenario scenario, HttpClient client, _) = await CreateAsync();
        GameSetGameDto game = scenario.Games[0];

        // LockedUtc set by hand, clock left well before the first kickoff: the job's verdict alone
        // has to be enough.
        await scenario.MarkLockedAsync(_app.Factory, _app.NowUtc.UtcDateTime);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");
    }

    private async Task<(PickWeekScenario Scenario, HttpClient Client, DateTimeOffset LockAtUtc)> CreateAsync()
    {
        _app.Set(ApiTestFixture.PinnedNowUtc);
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);

        DateTime lockAtUtc = await _app.Factory.QueryDbAsync(db => db.WeekGameSets
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.LockAtUtc!.Value)
            .SingleAsync());

        return (scenario, _app.Factory.CreateMutatingClientAs(scenario.MemberUserId), new DateTimeOffset(lockAtUtc, TimeSpan.Zero));
    }
}
