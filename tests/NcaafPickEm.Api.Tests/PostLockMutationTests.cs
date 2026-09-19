using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Everything a locked week refuses (Features 02, 03 and 04): picks and every piece of
/// configuration, both after <see cref="LockWeekJob"/> has run and in the window between
/// <c>LockAtUtc</c> and the job getting to it (D-089).
/// </summary>
/// <remarks>
/// <see cref="LockEnforcementTests"/> already covers picks in the pre-job window; this class is
/// about the configuration routes reaching the same conclusion, and about the whole set of them
/// going through the real job rather than a hand-written <c>LockedUtc</c>.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class PostLockMutationTests : IAsyncLifetime
{
    private readonly ClockedApp _app;

    public PostLockMutationTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData("generate")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("week-rules")]
    [InlineData("override")]
    public async Task GivenTheLockJobHasRun_WhenConfiguringTheWeek_ThenItIs409(string mutation)
    {
        Fixture week = await CreateAsync();
        await week.RunLockJobAsync();

        using HttpResponseMessage response = await week.MutateAsync(mutation);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");
    }

    /// <remarks>
    /// The window D-089 closes: <c>LockAtUtc</c> has arrived, the job has not run yet, and
    /// configuration must already be refused — otherwise a commissioner could change what a game
    /// is worth after the members stopped being able to pick it.
    /// </remarks>
    [Theory]
    [InlineData("generate")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("week-rules")]
    [InlineData("override")]
    public async Task GivenTheLockInstantHasPassedButTheJobHasNotRun_WhenConfiguringTheWeek_ThenItIsStill409(
        string mutation)
    {
        Fixture week = await CreateAsync();
        _app.Set(new DateTimeOffset(week.LockAtUtc, TimeSpan.Zero));

        using HttpResponseMessage response = await week.MutateAsync(mutation);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");

        // Really the pre-job window: nothing has written LockedUtc.
        (await week.LoadLockedUtcAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GivenTheLockJobHasRun_WhenSettingAPick_ThenItIs409()
    {
        Fixture week = await CreateAsync();
        await week.RunLockJobAsync();

        GameSetGameDto game = week.Scenario.Games[0];
        using HttpClient member = _app.Factory.CreateMutatingClientAs(week.Scenario.MemberUserId);

        using HttpResponseMessage response = await member.PutAsJsonAsync(
            $"{week.Scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");
    }

    [Fact]
    public async Task GivenTheLockJobHasRun_WhenSubmitting_ThenItIs409()
    {
        Fixture week = await CreateAsync();

        using HttpClient member = _app.Factory.CreateMutatingClientAs(week.Scenario.MemberUserId);
        foreach (GameSetGameDto game in week.Scenario.Games)
        {
            using HttpResponseMessage pick = await member.PutAsJsonAsync(
                $"{week.Scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
            pick.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await week.RunLockJobAsync();

        using HttpResponseMessage response = await member.PostAsync($"{week.Scenario.PicksRoute}/me/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Locked");
    }

    /// <remarks>
    /// The one configuration route that is not refused: replacing the league's point rules is a
    /// league-level save, so it succeeds and simply leaves the locked week's frozen values alone
    /// (04-Domain-Algorithms.md section 3).
    /// </remarks>
    [Fact]
    public async Task GivenTheLockJobHasRun_WhenReplacingLeaguePointRules_ThenItSucceedsAndTheWeekIsUntouched()
    {
        Fixture week = await CreateAsync();
        await week.RunLockJobAsync();

        Dictionary<Guid, int> before = await week.LoadPointValuesAsync();

        using HttpClient commish = _app.Factory.CreateMutatingClientAs(week.Scenario.CommissionerUserId);
        PointRuleDto[] rules =
        [
            new(null, 0, PointRuleType.CloseSpread, null, null, SpreadThreshold: 50m, PointValue: 88),
        ];

        using HttpResponseMessage response = await commish.PutAsJsonAsync(
            $"/api/leagues/{week.Scenario.LeagueId}/point-rules", rules);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await week.LoadPointValuesAsync()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task GivenTheLockInstantHasPassedButTheJobHasNotRun_WhenReplacingLeaguePointRules_ThenTheWeekIsUntouched()
    {
        Fixture week = await CreateAsync();

        Dictionary<Guid, int> before = await week.LoadPointValuesAsync();
        _app.Set(new DateTimeOffset(week.LockAtUtc, TimeSpan.Zero));

        using HttpClient commish = _app.Factory.CreateMutatingClientAs(week.Scenario.CommissionerUserId);
        PointRuleDto[] rules =
        [
            new(null, 0, PointRuleType.CloseSpread, null, null, SpreadThreshold: 50m, PointValue: 88),
        ];

        using HttpResponseMessage response = await commish.PutAsJsonAsync(
            $"/api/leagues/{week.Scenario.LeagueId}/point-rules", rules);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await week.LoadPointValuesAsync()).Should().BeEquivalentTo(before);
        (await week.LoadLockedUtcAsync()).Should().BeNull();
    }

    private async Task<Fixture> CreateAsync()
    {
        _app.Set(ApiTestFixture.PinnedNowUtc);
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);

        DateTime lockAtUtc = await _app.Factory.QueryDbAsync(db => db.WeekGameSets
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.LockAtUtc!.Value)
            .SingleAsync());

        Guid spareGameId = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005);

        return new Fixture(_app, scenario, lockAtUtc, spareGameId);
    }

    /// <summary>One league, one generated week, and every mutation that week should refuse.</summary>
    private sealed record Fixture(
        ClockedApp App,
        PickWeekScenario Scenario,
        DateTime LockAtUtc,
        Guid SpareGameId)
    {
        private string WeekRoute => $"/api/leagues/{Scenario.LeagueId}/weeks/{PickWeekScenario.Week}";

        /// <summary>Runs the real lock job at the week's lock instant.</summary>
        public async Task RunLockJobAsync()
        {
            App.Set(new DateTimeOffset(LockAtUtc, TimeSpan.Zero));

            await using AsyncServiceScope scope = App.Factory.Services
                .GetRequiredService<IServiceScopeFactory>()
                .CreateAsyncScope();

            LockWeekJob job = ActivatorUtilities.CreateInstance<LockWeekJob>(scope.ServiceProvider);
            await job.RunAsync(
                new OneShotOccurrence(
                    Scenario.WeekGameSetId.ToString("N"),
                    new DateTimeOffset(LockAtUtc, TimeSpan.Zero)),
                CancellationToken.None);

            (await LoadLockedUtcAsync()).Should().NotBeNull();
        }

        public async Task<HttpResponseMessage> MutateAsync(string mutation)
        {
            using HttpClient commish = App.Factory.CreateMutatingClientAs(Scenario.CommissionerUserId);

            return mutation switch
            {
                "generate" => await commish.PostAsync($"{WeekRoute}/gameset/generate", null),
                "add" => await commish.PostAsJsonAsync(
                    $"{WeekRoute}/gameset/games", new AddGameRequest(SpareGameId)),
                "remove" => await commish.DeleteAsync(
                    $"{WeekRoute}/gameset/games/{Scenario.Games[0].GameId}"),
                "week-rules" => await commish.PutAsJsonAsync(
                    $"{WeekRoute}/gameset-rules",
                    new WeekRulesResponse(
                        true,
                        [new GameSetRuleDto(null, GameSetRuleType.Top25, null, null, null, null, false, 0)])),
                "override" => await commish.PutAsJsonAsync(
                    $"{WeekRoute}/gameset/games/{Scenario.Games[0].GameId}/points",
                    new SetPointOverrideRequest(42)),
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation."),
            };
        }

        public async Task<DateTime?> LoadLockedUtcAsync() =>
            await App.Factory.QueryDbAsync(db => db.WeekGameSets
                .AsNoTracking()
                .Where(set => set.Id == Scenario.WeekGameSetId)
                .Select(set => set.LockedUtc)
                .SingleAsync());

        public async Task<Dictionary<Guid, int>> LoadPointValuesAsync() =>
            await App.Factory.QueryDbAsync(db => db.WeekGameSetGames
                .AsNoTracking()
                .Where(row => row.WeekGameSetId == Scenario.WeekGameSetId && !row.IsRemoved)
                .ToDictionaryAsync(row => row.Id, row => row.ResolvedPointValue));
    }
}
