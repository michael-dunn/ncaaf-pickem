using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/generate</c> (Feature 02, P3-03) against
/// the Week 7, 2026 fixture schedule.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class GameSetGenerateTests
{
    private readonly ApiTestFixture _fixture;

    public GameSetGenerateTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(League League, HttpClient Client)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);
        return (league, client);
    }

    private static async Task PutDefaultRulesAsync(HttpClient client, Guid leagueId, GameSetRuleDto[] rules)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{leagueId}/gameset-rules", rules);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenTheDefaultTop25Rule_WhenGenerating_ThenExactlyTheRankedTeamsGamesAreSelected()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);

        using HttpResponseMessage response = await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        body!.Games.Should().HaveCount(3);

        HashSet<string> schools = [.. body.Games.SelectMany(g => new[] { g.HomeTeam.School, g.AwayTeam.School })];
        schools.Should().BeEquivalentTo(["Michigan", "Texas", "Alabama", "Auburn", "Georgia", "Kentucky"]);
        body.Games.Should().OnlyContain(g => g.Source == GameSetGameSource.Rule);
        body.LockAtUtc.Should().NotBeNull();
        body.LockAtEasternDisplay.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenAChangedDefaultRule_WhenRegenerating_ThenTheDiffAddsAndRemovesGames()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid oklahomaId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900111);
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Team, null, null, oklahomaId, null, false, 0)]);

        using HttpResponseMessage response = await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        body!.Games.Should().ContainSingle(g => g.HomeTeam.School == "Oklahoma" || g.AwayTeam.School == "Oklahoma");
        body.Games.Should().NotContain(g => g.HomeTeam.School == "Michigan" || g.AwayTeam.School == "Michigan");

        // The previously rule-selected rows are marked removed, not deleted.
        Guid michiganTexasGameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700001);
        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame row = await db.WeekGameSetGames
                .SingleAsync(r => r.GameId == michiganTexasGameId && r.WeekGameSet!.LeagueId == league.Id);
            row.IsRemoved.Should().BeTrue();
            row.RemovedReason.Should().Be("Rule regeneration");
        });
    }

    [Fact]
    public async Task GivenNoChanges_WhenGeneratingTwice_ThenTheSecondRunChangesNoRow()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        List<(Guid Id, Guid GameId, bool IsRemoved, int PointValue)> before = await ReadRowsAsync(league.Id);

        using HttpResponseMessage second = await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        List<(Guid Id, Guid GameId, bool IsRemoved, int PointValue)> after = await ReadRowsAsync(league.Id);

        after.Should().BeEquivalentTo(before, "a regeneration over unchanged inputs must not add, remove, or re-value a row");
        after.Should().OnlyContain(row => !row.IsRemoved);
    }

    private async Task<List<(Guid Id, Guid GameId, bool IsRemoved, int PointValue)>> ReadRowsAsync(Guid leagueId) =>
        await _fixture.Factory.QueryDbAsync(async db =>
        {
            List<WeekGameSetGame> rows = await db.WeekGameSetGames
                .AsNoTracking()
                .Where(r => r.WeekGameSet!.LeagueId == leagueId)
                .OrderBy(r => r.Id)
                .ToListAsync();

            return rows.ConvertAll(r => (r.Id, r.GameId, r.IsRemoved, r.ResolvedPointValue));
        });

    [Fact]
    public async Task GivenAManuallyAddedGame_WhenRegenerating_ThenTheManualRowSurvives()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);
        await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        Guid ohioStateGameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005); // Ohio State / Wisconsin
        using HttpResponseMessage addResponse = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games", new AddGameRequest(ohioStateGameId));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid oklahomaId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900111);
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Team, null, null, oklahomaId, null, false, 0)]);

        using HttpResponseMessage response = await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);
        WeekGameSetResponse? body = await response.Content.ReadFromJsonAsync<WeekGameSetResponse>();

        body!.Games.Should().Contain(g => g.GameId == ohioStateGameId && g.Source == GameSetGameSource.Manual);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenGenerating_ThenItIs409()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = 7,
                UsesOverride = false,
                GeneratedUtc = DateTime.UtcNow,
                LockedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        using HttpResponseMessage response = await client.PostAsync($"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenRulesThatSelectMoreThanFiftyGames_WhenGenerating_ThenItIs409WithTheCount()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await _fixture.Factory.QueryDbAsync(async db =>
        {
            var created = new League
            {
                Id = Guid.CreateVersion7(),
                Name = $"Overflow League {Guid.CreateVersion7().ToString()[..8]}",
                SeasonYear = 2099,
                FirstWeek = 1,
                LastWeek = 1,
                DefaultPointValue = 10,
                CreatedByUserId = commissioner.Id,
                CreatedUtc = DateTime.UtcNow,
            };
            db.Leagues.Add(created);
            await db.SaveChangesAsync();
            return created;
        });
        await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        Guid michiganId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900101);
        Guid conferenceId = await FixtureGameData.GetConferenceIdAsync(_fixture.Factory, 5); // Michigan's conference (B1G)
        Guid otherTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900102); // Texas, any other FBS team

        // A Saturday-Eastern kickoff for the synthetic 2099/week 1 season.
        DateTime kickoffUtc = new(2099, 1, 3, 17, 0, 0, DateTimeKind.Utc); // 2099-01-03 is a Saturday.

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            for (int i = 0; i < 55; i++)
            {
                db.Games.Add(new Game
                {
                    Id = Guid.CreateVersion7(),
                    CfbdGameId = 90_000_000 + i,
                    SeasonYear = 2099,
                    Week = 1,
                    HomeTeamId = michiganId,
                    AwayTeamId = otherTeamId,
                    KickoffUtc = kickoffUtc,
                    KickoffEasternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(new DateTimeOffset(kickoffUtc)).DateTime),
                    IsSaturdayEastern = true,
                    IsConferenceGame = false,
                    Status = GameStatus.Scheduled,
                });
            }

            await db.SaveChangesAsync();
        });

        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);
        await PutDefaultRulesAsync(client, league.Id, [new(null, GameSetRuleType.Conference, conferenceId, null, null, null, false, 0)]);

        using HttpResponseMessage previewResponse = await client.PostAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/1/gameset/preview",
            new GameSetRuleDto[] { new(null, GameSetRuleType.Conference, conferenceId, null, null, null, false, 0) });
        GameSetPreview? preview = await previewResponse.Content.ReadFromJsonAsync<GameSetPreview>();
        preview!.ExceedsMax.Should().BeTrue();
        preview.Count.Should().Be(55);

        using HttpResponseMessage response = await client.PostAsync($"/api/leagues/{league.Id}/weeks/1/gameset/generate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        System.Text.Json.Nodes.JsonNode? problem = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        problem!["count"]!.GetValue<int>().Should().Be(55);
    }
}
