using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Point-rule and per-game override endpoints (Feature 03, P3-03) against the Week 7, 2026
/// fixture schedule and lines.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class PointRulesEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public PointRulesEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(League League, HttpClient Client, Guid CommissionerMembershipId)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        User commissioner = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Commish"));
        League league = await _fixture.Factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        Membership membership = await _fixture.Factory.QueryDbAsync(
            db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _fixture.Factory.CreateMutatingClientAs(commissioner.Id);
        return (league, client, membership.Id);
    }

    private async Task AddGameAsync(HttpClient client, Guid leagueId, int week, long cfbdGameId)
    {
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, cfbdGameId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{leagueId}/weeks/{week}/gameset/games", new AddGameRequest(gameId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAConferenceRule_WhenPuttingPointRules_ThenTheConferenceGameIsElevated()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700002); // Maryland / Rutgers, IsConferenceGame = true
        await AddGameAsync(client, league.Id, 7, 700005); // Ohio State / Wisconsin, not a conference game

        PointRuleDto[] rules = [new(null, 0, PointRuleType.ConferenceGame, null, null, null, 20)];
        using HttpResponseMessage putResponse = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/point-rules", rules);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset");
        WeekGameSetResponse? body = await getResponse.Content.ReadFromJsonAsync<WeekGameSetResponse>();

        GameSetGameDto conferenceGame = body!.Games.Single(g => g.HomeTeam.School == "Maryland");
        conferenceGame.PointValue.Should().Be(20);
        conferenceGame.IsPointValueElevated.Should().BeTrue();

        GameSetGameDto nonConferenceGame = body.Games.Single(g => g.HomeTeam.School == "Ohio State");
        nonConferenceGame.PointValue.Should().Be(league.DefaultPointValue);
        nonConferenceGame.IsPointValueElevated.Should().BeFalse();
    }

    [Fact]
    public async Task GivenACloseSpreadRule_WhenPuttingPointRules_ThenTheCloseGameIsElevated()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700008); // Oklahoma / Missouri, spread 2.5

        PointRuleDto[] rules = [new(null, 0, PointRuleType.CloseSpread, null, null, 3m, 25)];
        await client.PutAsJsonAsync($"/api/leagues/{league.Id}/point-rules", rules);

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset");
        WeekGameSetResponse? body = await getResponse.Content.ReadFromJsonAsync<WeekGameSetResponse>();

        GameSetGameDto closeGame = body!.Games.Single(g => g.HomeTeam.School == "Oklahoma");
        closeGame.PointValue.Should().Be(25);
        closeGame.IsPointValueElevated.Should().BeTrue();
    }

    [Fact]
    public async Task GivenALockedWeek_WhenPuttingPointRules_ThenTheLockedWeekIsUntouchedButOthersReresolve()
    {
        (League league, HttpClient client, Guid membershipId) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700005);

        Guid lockedGameSetId = Guid.CreateVersion7();
        Guid lockedGameSetGameId = Guid.CreateVersion7();
        Guid ohioStateGameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(new WeekGameSet
            {
                Id = lockedGameSetId,
                LeagueId = league.Id,
                Week = 6,
                UsesOverride = false,
                GeneratedUtc = DateTime.UtcNow,
                LockedUtc = DateTime.UtcNow,
            });
            db.WeekGameSetGames.Add(new WeekGameSetGame
            {
                Id = lockedGameSetGameId,
                WeekGameSetId = lockedGameSetId,
                GameId = ohioStateGameId,
                Source = GameSetGameSource.Manual,
                IsRemoved = false,
                ResolvedPointValue = 99,
            });
            await db.SaveChangesAsync();
        });

        Guid ohioStateTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900105);
        PointRuleDto[] rules = [new(null, 0, PointRuleType.Team, null, ohioStateTeamId, null, 42)];
        using HttpResponseMessage putResponse = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/point-rules", rules);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame lockedRow = await db.WeekGameSetGames.SingleAsync(r => r.Id == lockedGameSetGameId);
            lockedRow.ResolvedPointValue.Should().Be(99, "a locked week's resolved point values must never change");
        });

        // ... while the unlocked week the same game sits in did pick the new rule up.
        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset");
        WeekGameSetResponse? body = await getResponse.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        body!.Games.Single(g => g.GameId == ohioStateGameId).PointValue.Should().Be(42);
    }

    [Fact]
    public async Task GivenAnExistingPick_WhenReResolvingPointValues_ThenThePickIsUnaffected()
    {
        (League league, HttpClient client, Guid membershipId) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700005);

        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);
        Guid michiganId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900101);
        Guid gameSetGameId = await _fixture.Factory.QueryDbAsync(
            db => db.WeekGameSetGames.Where(r => r.GameId == gameId && r.WeekGameSet!.LeagueId == league.Id).Select(r => r.Id).SingleAsync());

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            db.Picks.Add(new Pick
            {
                Id = Guid.CreateVersion7(),
                MembershipId = membershipId,
                WeekGameSetGameId = gameSetGameId,
                PickedTeamId = michiganId,
                UpdatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        Guid ohioStateTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900105);
        PointRuleDto[] rules = [new(null, 0, PointRuleType.Team, null, ohioStateTeamId, null, 33)];
        using HttpResponseMessage putResponse = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/point-rules", rules);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame row = await db.WeekGameSetGames.SingleAsync(r => r.Id == gameSetGameId);
            row.ResolvedPointValue.Should().Be(33, "the rule change must re-resolve the unlocked week");

            Pick pick = await db.Picks.SingleAsync(p => p.WeekGameSetGameId == gameSetGameId);
            pick.PickedTeamId.Should().Be(michiganId);
        });
    }

    [Fact]
    public async Task GivenAnOverride_WhenPuttingAndClearing_ThenTheResolvedValueTracksIt()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700005);
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);

        using HttpResponseMessage setResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{gameId}/points", new SetPointOverrideRequest(75));
        setResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        GameSetGameDto? overridden = await setResponse.Content.ReadFromJsonAsync<GameSetGameDto>();
        overridden!.PointValue.Should().Be(75);

        using HttpResponseMessage clearResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{gameId}/points", new SetPointOverrideRequest(null));
        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        GameSetGameDto? cleared = await clearResponse.Content.ReadFromJsonAsync<GameSetGameDto>();
        cleared!.PointValue.Should().Be(league.DefaultPointValue);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenSettingAnOverride_ThenItIs409()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700005);
        Guid gameId = await FixtureGameData.GetGameIdAsync(_fixture.Factory, 700005);

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            WeekGameSet set = await db.WeekGameSets.SingleAsync(s => s.LeagueId == league.Id && s.Week == 7);
            set.LockedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/games/{gameId}/points", new SetPointOverrideRequest(50));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAChangedLeagueDefault_WhenUpdatingSettings_ThenUnlockedWeeksReResolveImmediately()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();
        await AddGameAsync(client, league.Id, 7, 700005); // no rule matches it, so it sits on the default

        using HttpResponseMessage settingsResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/settings",
            new UpdateLeagueSettingsRequest(league.Name, league.FirstWeek, league.LastWeek, 30));
        settingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset");
        WeekGameSetResponse? body = await getResponse.Content.ReadFromJsonAsync<WeekGameSetResponse>();

        GameSetGameDto game = body!.Games.Single(g => g.HomeTeam.School == "Ohio State");
        game.PointValue.Should().Be(30, "changing the league default must re-resolve every unlocked week at once");
        game.IsPointValueElevated.Should().BeFalse("a game sitting on the default is never elevated");
    }

    [Fact]
    public async Task GivenAnInvalidRuleSet_WhenPuttingPointRules_ThenItIs400WithFieldKeys()
    {
        (League league, HttpClient client, _) = await CreateCommishLeagueAsync();

        PointRuleDto[] rules =
        [
            new(null, 0, PointRuleType.Team, null, null, null, 500), // out of range
            new(null, 0, PointRuleType.ConferenceGame, null, null, null, 10), // duplicate priority
        ];

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/point-rules", rules);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("Rules[0].PointValue");
        problem.Errors.Should().ContainKey("Rules[0].TeamId");
        problem.Errors.Should().ContainKey("Rules[1].Priority");
    }
}
