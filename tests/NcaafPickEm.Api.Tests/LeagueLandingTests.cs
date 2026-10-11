using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>LeagueSummary.Landing</c> on <c>GET /api/leagues</c> (D-187): the service hands the rule
/// the league's current week's set and the one before it, at the app's clock. The rule's cases
/// are <c>LeagueLandingRuleTests</c>; these prove the wiring.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LeagueLandingTests : IAsyncLifetime
{
    // The fixture calendar's Week 7 runs Sun 11 Oct to Sat 17 Oct 2026 ET (EDT, UTC-4).
    private static readonly DateTime Week7LockAt = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Week7SaturdayAfternoon = new(2026, 10, 17, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Week8SundayNoon = new(2026, 10, 18, 16, 0, 0, TimeSpan.Zero);

    private readonly ClockedApp _app;

    public LeagueLandingTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GivenAWeekThatHasNotLocked_WhenListingMyLeagues_ThenTheLeagueLandsOnPicks()
    {
        _app.Set(ApiTestFixture.PinnedNowUtc);
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_app.Factory);
        await AddSetAsync(scenario, 7, Week7LockAt);

        (await ListAsync(scenario)).Landing.Should().Be(LeagueLanding.Picks);
    }

    [Fact]
    public async Task GivenALockedWeekStillBeingPlayed_WhenListingMyLeagues_ThenTheLeagueLandsOnTheDashboard()
    {
        _app.Set(Week7SaturdayAfternoon);
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_app.Factory);
        await AddSetAsync(scenario, 7, Week7LockAt, lockedUtc: Week7LockAt);

        (await ListAsync(scenario)).Landing.Should().Be(LeagueLanding.Dashboard);
    }

    [Fact]
    public async Task GivenTheSundayAfterAPlayedWeek_WhenListingMyLeagues_ThenTheLeagueLandsOnTheLeaderboard()
    {
        _app.Set(Week8SundayNoon);
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_app.Factory);
        await AddSetAsync(scenario, 7, Week7LockAt, lockedUtc: Week7LockAt, isComplete: true);
        await AddSetAsync(scenario, 8, Week7LockAt.AddDays(7));

        LeagueSummary league = await ListAsync(scenario);

        league.CurrentWeek.Should().Be(8);
        league.Landing.Should().Be(LeagueLanding.Leaderboard);
    }

    [Fact]
    public async Task GivenTheMondayAfterAPlayedWeek_WhenReadingTheLeague_ThenTheDisplayWeekIsTheWeekJustPlayed()
    {
        // Feature 15 (D-193): Monday 19 Oct 23:59 EDT, before week 8's picks open on Tuesday at 04:00.
        _app.Set(new DateTimeOffset(2026, 10, 20, 3, 59, 0, TimeSpan.Zero));
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_app.Factory);
        await AddSetAsync(scenario, 7, Week7LockAt, lockedUtc: Week7LockAt, isComplete: true);
        await AddSetAsync(scenario, 8, Week7LockAt.AddDays(7));
        using HttpClient client = _app.Factory.CreateClientAs(scenario.MemberUserId);

        LeagueSummary summary = await ListAsync(scenario);
        summary.CurrentWeek.Should().Be(8);
        summary.DisplayWeek.Should().Be(7);
        summary.Landing.Should().Be(LeagueLanding.Leaderboard);

        LeagueDetail? detail = await client.GetFromJsonAsync<LeagueDetail>($"/api/leagues/{scenario.LeagueId}");
        detail!.CurrentWeek.Should().Be(8);
        detail.DisplayWeek.Should().Be(7);

        LeagueWeek[]? weeks = await client.GetFromJsonAsync<LeagueWeek[]>($"/api/leagues/{scenario.LeagueId}/weeks");
        weeks!.Single(week => week.IsDisplayWeek).Week.Should().Be(7);
        weeks!.Single(week => week.IsCurrent).Week.Should().Be(8);

        // Tuesday 04:00 EDT: week 8's picks open, and the app moves on to it.
        _app.Set(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
        LeagueSummary tuesday = await ListAsync(scenario);
        tuesday.DisplayWeek.Should().Be(8);
        tuesday.Landing.Should().Be(LeagueLanding.Picks);
    }

    private async Task<LeagueSummary> ListAsync(LeagueScenario scenario)
    {
        using HttpClient client = _app.Factory.CreateClientAs(scenario.MemberUserId);
        LeagueSummary[]? mine = await client.GetFromJsonAsync<LeagueSummary[]>("/api/leagues");
        return mine.Should().ContainSingle().Subject;
    }

    private Task AddSetAsync(
        LeagueScenario scenario,
        int week,
        DateTime lockAtUtc,
        DateTime? lockedUtc = null,
        bool isComplete = false) =>
        _app.Factory.ExecuteDbAsync(database =>
        {
            database.WeekGameSets.Add(new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = scenario.LeagueId,
                Week = week,
                GeneratedUtc = lockAtUtc.AddDays(-5),
                LockAtUtc = lockAtUtc,
                LockedUtc = lockedUtc,
                IsComplete = isComplete,
            });
            return database.SaveChangesAsync();
        });
}
