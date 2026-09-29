using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Simulator;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Contracts.Dashboard;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Engine;
using NcaafPickEm.Simulator.Engine.Views;
using NcaafPickEm.Simulator.Tests.Infrastructure;

namespace NcaafPickEm.Simulator.Tests;

/// <summary>
/// The point of the whole exercise: the real app, in Simulator mode over the simulator's database,
/// on a Tuesday, sees exactly the week the simulator made - current week, set, lock, picks - and
/// behaves as it would on a Saturday: picks through the API count, the lock opens everyone's
/// picks and the dashboard, and autoplay's scores land on it.
/// </summary>
public sealed class AppInSimulatorModeTests
{
    [Fact]
    public async Task GivenASandbox_WhenTheTesterSignsInToTheApp_ThenTheAppShowsTheSimulatedWeekAndTakesTheirPicks()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        SimSnapshot snapshot = await sim.CreateSandboxAsync();
        SimLeagueView league = snapshot.Leagues[0];

        await using var app = new SimulatorModeApp(sim.Database.ConnectionString, sim.Clock);
        using HttpClient michael = await app.SignInAsync("michael");

        LeagueDetail? detail = await michael.GetFromJsonAsync<LeagueDetail>($"/api/leagues/{league.LeagueId}");
        detail!.CurrentWeek.Should().Be(1);
        detail.MyRole.Should().Be(MembershipRole.Commissioner);
        detail.CurrentWeekLockAtUtc.Should().Be(league.Set!.LockAtUtc);

        MyPicksResponse? mine = await michael.GetFromJsonAsync<MyPicksResponse>($"/api/leagues/{league.LeagueId}/weeks/1/picks/me");
        mine!.TotalCount.Should().Be(league.Set.ActiveGames.Count());

        Guid gameId = mine.Games[0].Game.GameId;
        Guid teamId = mine.Games[0].Game.AwayTeam.TeamId;
        using HttpResponseMessage pick = await michael.PutAsync($"/api/leagues/{league.LeagueId}/weeks/1/picks/me/{gameId}", JsonContent.Create(new SetPickRequest(teamId)));
        pick.StatusCode.Should().Be(HttpStatusCode.OK, await pick.Content.ReadAsStringAsync());

        snapshot = await sim.SnapshotAsync();
        snapshot.Leagues[0].Members.Single(member => member.IsTester).Picks[gameId].Should().Be(teamId,
            "a pick made in the app is the pick the simulator sees");
    }

    [Fact]
    public async Task GivenPicksMadeInTheSimulator_WhenTheLockPasses_ThenTheAppOpensEveryonesPicksAndTheDashboardScoresLive()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        SimSnapshot snapshot = await sim.CreateSandboxAsync();
        Guid leagueId = snapshot.Leagues[0].LeagueId;

        await using var app = new SimulatorModeApp(sim.Database.ConnectionString, sim.Clock);
        using HttpClient alyson = await app.SignInAsync("alyson");

        using (HttpResponseMessage hidden = await alyson.GetAsync($"/api/leagues/{leagueId}/weeks/1/picks"))
        {
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, "picks are private until the week locks");
        }

        SimHarness.Ok(await sim.World.FillEveryoneAsync(PickStrategy.Mixed, includeTester: true, submit: true, leaveOneIncomplete: false, CancellationToken.None));
        SimHarness.Ok(await sim.World.LockNowAsync(CancellationToken.None));
        await sim.PassAsync(TimeSpan.FromMinutes(6));

        using (HttpResponseMessage open = await alyson.GetAsync($"/api/leagues/{leagueId}/weeks/1/picks"))
        {
            open.StatusCode.Should().Be(HttpStatusCode.OK, "the lock job ran and everyone's picks are visible");
        }

        DashboardResponse? dashboard = await alyson.GetFromJsonAsync<DashboardResponse>($"/api/leagues/{leagueId}/weeks/1/dashboard");
        dashboard!.IsAvailable.Should().BeTrue();
        dashboard.Games.Concat(dashboard.EveryoneAgrees).Should().Contain(game => game.Game.Status == GameStatus.InProgress,
            "autoplay has the first window's games under way");
        dashboard.ScoresMayBeStale.Should().BeFalse();
    }

    [Fact]
    public async Task GivenSimulatorMode_WhenTheAppStarts_ThenItPollsAndIngestsNothingAndSaysWhoOwnsTheData()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        SimSnapshot snapshot = await sim.CreateSandboxAsync();

        await using var app = new SimulatorModeApp(sim.Database.ConnectionString, sim.Clock);
        using HttpClient michael = await app.SignInAsync("michael");

        app.Services.GetServices<IHostedService>().Should().NotContain(service => service is SaturdayPoller);
        using (IServiceScope scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetServices<IScheduledJob>().Select(job => job.Name)
                .Should().NotContain(name => name.Contains("Refresh", StringComparison.Ordinal));
        }

        DataStatusResponse? status = await michael.GetFromJsonAsync<DataStatusResponse>("/api/admin/data-status");
        status!.ActiveLiveScoreSource.Should().Be("Simulator");

        using HttpResponseMessage refresh = await michael.PostAsync("/api/admin/refresh/schedule", content: null);
        ManualRefreshResponse? result = await refresh.Content.ReadFromJsonAsync<ManualRefreshResponse>();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("owned by NcaafPickEm.Simulator");

        (await sim.SnapshotAsync()).Games.Should().HaveCount(snapshot.Games.Count, "a refused refresh writes nothing");
        SimulatorOwnedReferenceDataProvider.OwnedBySimulatorMessage.Should().StartWith("Reference data is owned");
    }
}
