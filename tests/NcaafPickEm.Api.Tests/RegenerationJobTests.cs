using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="RegenerateGameSetsJob"/> (Feature 02, P3-04), driven directly against a
/// <see cref="ClockedApp"/> at the job's real slot, Tuesday 2026-10-13 03:30 ET, so "current week"
/// is deterministically week 7 of the fixture calendar and its picks have not opened yet
/// (Feature 16: they open at 04:00).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class RegenerationJobTests : IAsyncLifetime
{
    /// <summary>Tuesday 2026-10-13 03:30 EDT, week 7's regeneration slot.</summary>
    private static readonly DateTimeOffset TuesdaySlotUtc = new(2026, 10, 13, 7, 30, 0, TimeSpan.Zero);

    private readonly ClockedApp _app;

    public RegenerationJobTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture, TuesdaySlotUtc);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private async Task<(League League, HttpClient Client)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_app.Factory);

        User commissioner = await _app.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "RegenCommish"));
        League league = await _app.Factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _app.Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _app.Factory.CreateMutatingClientAs(commissioner.Id);
        return (league, client);
    }

    private static async Task PutTop25RuleAsync(HttpClient client, Guid leagueId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{leagueId}/gameset-rules",
            new GameSetRuleDto[] { new(null, GameSetRuleType.Top25, null, null, null, null, false, 0) });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task RunJobAsync()
    {
        await using AsyncServiceScope scope = _app.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        RegenerateGameSetsJob job = ActivatorUtilities.CreateInstance<RegenerateGameSetsJob>(scope.ServiceProvider);
        await job.RunAsync(_app.NowUtc, CancellationToken.None);
    }

    private async Task<WeekGameSetResponse> GetWeekAsync(HttpClient client, Guid leagueId, int week)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{leagueId}/weeks/{week}/gameset");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WeekGameSetResponse>())!;
    }

    [Fact]
    public async Task GivenANewlyRankedTeam_WhenTheTuesdayJobRuns_ThenItsGameIsAdded()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        WeekGameSetResponse before = await GetWeekAsync(client, league.Id, 7);
        before.Games.Should().NotContain(g => g.HomeTeam.School == "Oklahoma" || g.AwayTeam.School == "Oklahoma");

        // Oklahoma (cfbdTeamId 900111) was unranked in the fixture's week-7 AP poll; rank it 12th,
        // simulating what P2-04's rankings-refresh job would have just written. The database is
        // shared for the whole test run (other tests, e.g. GameSetGenerateTests, assert exact
        // counts against the fixture's original 11-team week-7 poll), so this row is removed
        // again in a finally block.
        Guid oklahomaTeamId = await FixtureGameData.GetTeamIdAsync(_app.Factory, 900111);
        try
        {
            await _app.Factory.QueryDbAsync(async db =>
            {
                db.Rankings.Add(new NcaafPickEm.Domain.Seasons.Ranking
                {
                    SeasonYear = 2026,
                    Week = 7,
                    Poll = NcaafPickEm.Domain.Seasons.Ranking.ApPoll,
                    Rank = 12,
                    TeamId = oklahomaTeamId,
                    FetchedUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
                return true;
            });

            await RunJobAsync();

            WeekGameSetResponse after = await GetWeekAsync(client, league.Id, 7);
            after.Games.Should().Contain(g => g.HomeTeam.School == "Oklahoma" || g.AwayTeam.School == "Oklahoma");
        }
        finally
        {
            await _app.Factory.ExecuteDbAsync(async db =>
            {
                await db.Rankings
                    .Where(r => r.SeasonYear == 2026 && r.Week == 7 && r.TeamId == oklahomaTeamId)
                    .ExecuteDeleteAsync();
            });
        }
    }

    [Fact]
    public async Task GivenPicksHaveOpened_WhenTheJobRunsLate_ThenTheSetIsNotChanged()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);
        WeekGameSetResponse before = await GetWeekAsync(client, league.Id, 7);

        Guid oklahomaTeamId = await FixtureGameData.GetTeamIdAsync(_app.Factory, 900111);
        try
        {
            await _app.Factory.QueryDbAsync(async db =>
            {
                db.Rankings.Add(new NcaafPickEm.Domain.Seasons.Ranking
                {
                    SeasonYear = 2026,
                    Week = 7,
                    Poll = NcaafPickEm.Domain.Seasons.Ranking.ApPoll,
                    Rank = 12,
                    TeamId = oklahomaTeamId,
                    FetchedUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
                return true;
            });

            // Tuesday 04:00 ET exactly: week 7's picks are open (Feature 16), so a catch-up run of
            // the 03:30 job must not add the newly ranked team's game.
            _app.Set(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
            await RunJobAsync();

            WeekGameSetResponse after = await GetWeekAsync(client, league.Id, 7);
            after.Games.Select(g => g.GameId).Should().BeEquivalentTo(before.Games.Select(g => g.GameId));
        }
        finally
        {
            await _app.Factory.ExecuteDbAsync(async db =>
            {
                await db.Rankings
                    .Where(r => r.SeasonYear == 2026 && r.Week == 7 && r.TeamId == oklahomaTeamId)
                    .ExecuteDeleteAsync();
            });
        }
    }

    [Fact]
    public async Task GivenAPostponedGame_WhenTheTuesdayJobRuns_ThenItIsRemovedAsAScheduleChange()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        WeekGameSetResponse before = await GetWeekAsync(client, league.Id, 7);
        before.Games.Should().Contain(g => g.HomeTeam.School == "Michigan" || g.AwayTeam.School == "Michigan");

        // Game 700001 (Michigan/Texas) is on the shared fixture schedule every other test in the
        // run also reads; restore it to Scheduled once this test is done.
        Guid gameId = await FixtureGameData.GetGameIdAsync(_app.Factory, 700001); // Michigan/Texas
        try
        {
            await _app.Factory.QueryDbAsync(async db =>
            {
                await db.Games.Where(g => g.Id == gameId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.Status, GameStatus.Postponed));
                return true;
            });

            await RunJobAsync();

            WeekGameSetResponse after = await GetWeekAsync(client, league.Id, 7);
            after.Games.Should().NotContain(g => g.HomeTeam.School == "Michigan" || g.AwayTeam.School == "Michigan");

            WeekGameSetGame row = await _app.Factory.QueryDbAsync(db => db.WeekGameSetGames
                .AsNoTracking()
                .Where(r => r.WeekGameSet!.LeagueId == league.Id && r.GameId == gameId)
                .SingleAsync());
            row.IsRemoved.Should().BeTrue();
            row.RemovedReason.Should().Be("Schedule change");
        }
        finally
        {
            await _app.Factory.ExecuteDbAsync(async db =>
            {
                await db.Games.Where(g => g.Id == gameId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.Status, GameStatus.Scheduled));
            });
        }
    }

    [Fact]
    public async Task GivenAManuallyAddedGame_WhenTheTuesdayJobRuns_ThenItSurvives()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid manualGameId = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005); // unranked, non-conference
        using HttpResponseMessage addResponse = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games",
            new AddGameRequest(manualGameId));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await RunJobAsync();

        WeekGameSetResponse after = await GetWeekAsync(client, league.Id, 7);
        after.Games.Should().Contain(g => g.Source == GameSetGameSource.Manual);
    }

    [Fact]
    public async Task GivenAnAlreadyLockedWeek_WhenTheTuesdayJobRuns_ThenItIsSkippedWithoutError()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        // Week 8 is the job's "next" target for the pinned current week (7). Lock it directly,
        // the way P4-02's lock job eventually will.
        WeekGameSet week8 = await _app.Factory.QueryDbAsync(async db =>
        {
            WeekGameSet? set = await db.WeekGameSets.FirstOrDefaultAsync(s => s.LeagueId == league.Id && s.Week == 8);
            if (set is null)
            {
                set = new WeekGameSet
                {
                    Id = Guid.CreateVersion7(),
                    LeagueId = league.Id,
                    Week = 8,
                    UsesOverride = false,
                    GeneratedUtc = DateTime.UtcNow,
                };
                db.WeekGameSets.Add(set);
            }

            set.LockedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return set;
        });

        Func<Task> act = RunJobAsync;
        await act.Should().NotThrowAsync();

        WeekGameSet stillLocked = await _app.Factory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .SingleAsync(s => s.Id == week8.Id));
        stillLocked.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task GivenTheNextWeekHasNoGames_WhenTheTuesdayJobRuns_ThenItsSetIsEmptyWithoutError()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);

        Func<Task> act = RunJobAsync;
        await act.Should().NotThrowAsync();

        WeekGameSetResponse week8 = await GetWeekAsync(client, league.Id, 8);
        week8.Games.Should().BeEmpty();
    }
}
