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
/// <see cref="RegenerateGameSetsJob"/> (Feature 02, P3-04), driven directly against
/// <see cref="ApiTestFixture.PinnedFactory"/> so "current week" is deterministically week 7 of
/// the Week 7, 2026 fixture calendar.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class RegenerationJobTests
{
    private readonly ApiTestFixture _fixture;

    public RegenerationJobTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(League League, HttpClient Client)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.PinnedFactory);

        User commissioner = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "RegenCommish"));
        League league = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(commissioner.Id);
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
        await using AsyncServiceScope scope = _fixture.PinnedFactory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        RegenerateGameSetsJob job = ActivatorUtilities.CreateInstance<RegenerateGameSetsJob>(scope.ServiceProvider);
        await job.RunAsync(ApiTestFixture.PinnedNowUtc, CancellationToken.None);
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
        // simulating what P2-04's rankings-refresh job would have just written.
        Guid oklahomaTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.PinnedFactory, 900111);
        await _fixture.PinnedFactory.QueryDbAsync(async db =>
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

    [Fact]
    public async Task GivenAPostponedGame_WhenTheTuesdayJobRuns_ThenItIsRemovedAsAScheduleChange()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        WeekGameSetResponse before = await GetWeekAsync(client, league.Id, 7);
        before.Games.Should().Contain(g => g.HomeTeam.School == "Michigan" || g.AwayTeam.School == "Michigan");

        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.PinnedFactory, 700001); // Michigan/Texas
        await _fixture.PinnedFactory.QueryDbAsync(async db =>
        {
            await db.Games.Where(g => g.Id == gameId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.Status, GameStatus.Postponed));
            return true;
        });

        await RunJobAsync();

        WeekGameSetResponse after = await GetWeekAsync(client, league.Id, 7);
        after.Games.Should().NotContain(g => g.HomeTeam.School == "Michigan" || g.AwayTeam.School == "Michigan");

        WeekGameSetGame row = await _fixture.PinnedFactory.QueryDbAsync(db => db.WeekGameSetGames
            .AsNoTracking()
            .Where(r => r.WeekGameSet!.LeagueId == league.Id && r.GameId == gameId)
            .SingleAsync());
        row.IsRemoved.Should().BeTrue();
        row.RemovedReason.Should().Be("Schedule change");
    }

    [Fact]
    public async Task GivenAManuallyAddedGame_WhenTheTuesdayJobRuns_ThenItSurvives()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutTop25RuleAsync(client, league.Id);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid manualGameId = await FixtureGameData.GetGameIdAsync(_fixture.PinnedFactory, 700005); // unranked, non-conference
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
        WeekGameSet week8 = await _fixture.PinnedFactory.QueryDbAsync(async db =>
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

        WeekGameSet stillLocked = await _fixture.PinnedFactory.QueryDbAsync(db => db.WeekGameSets
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
