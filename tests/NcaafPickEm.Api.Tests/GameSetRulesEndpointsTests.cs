using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Default and week-override game-set rules (Feature 02, P3-03):
/// <c>/api/leagues/{leagueId}/gameset-rules</c> and
/// <c>/api/leagues/{leagueId}/weeks/{week}/gameset-rules</c>.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class GameSetRulesEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public GameSetRulesEndpointsTests(ApiTestFixture fixture)
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

    [Fact]
    public async Task GivenNoRulesSaved_WhenGettingDefaultRules_ThenTheArrayIsEmpty()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{league.Id}/gameset-rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GameSetRuleDto[]? rules = await response.Content.ReadFromJsonAsync<GameSetRuleDto[]>();
        rules.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenValidRules_WhenPuttingDefaultRules_ThenTheyRoundTripWithEchoedNames()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        Guid secId = await FixtureGameData.GetConferenceIdAsync(_fixture.Factory, 8);
        Guid michiganId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900101);

        GameSetRuleDto[] rules =
        [
            new(null, GameSetRuleType.Top25, null, null, null, null, false, 0),
            new(null, GameSetRuleType.Conference, secId, null, null, null, true, 1),
            new(null, GameSetRuleType.Team, null, null, michiganId, null, false, 2),
        ];

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/gameset-rules", rules);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GameSetRuleDto[]? saved = await response.Content.ReadFromJsonAsync<GameSetRuleDto[]>();
        saved.Should().HaveCount(3);
        saved![1].ConferenceName.Should().Be("Southeastern Conference");
        saved[2].TeamName.Should().Be("Michigan");
        saved.Select(r => r.SortOrder).Should().Equal(0, 1, 2);
        saved.Should().OnlyContain(r => r.RuleId != null);

        // Round trip: GET returns exactly what was saved.
        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/gameset-rules");
        GameSetRuleDto[]? fetched = await getResponse.Content.ReadFromJsonAsync<GameSetRuleDto[]>();
        fetched.Should().HaveCount(3);
    }

    [Fact]
    public async Task GivenAConferenceRuleWithNoConference_WhenPuttingDefaultRules_ThenItIs400WithAFieldKey()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        GameSetRuleDto[] rules = [new(null, GameSetRuleType.Conference, null, null, null, null, false, 0)];

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/gameset-rules", rules);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("Rules[0].ConferenceId");
    }

    [Fact]
    public async Task GivenATeamRuleNamingAnFcsTeam_WhenPuttingDefaultRules_ThenItIs400()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();
        Guid fcsTeamId = await FixtureGameData.GetTeamIdAsync(_fixture.Factory, 900122); // Youngstown State, FCS

        GameSetRuleDto[] rules = [new(null, GameSetRuleType.Team, null, null, fcsTeamId, null, false, 0)];

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/gameset-rules", rules);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenNoWeekOverride_WhenGettingWeekRules_ThenItEchoesTheDefaultReadOnly()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        GameSetRuleDto[] defaults = [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];
        await client.PutAsJsonAsync($"/api/leagues/{league.Id}/gameset-rules", defaults);

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset-rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekRulesResponse? body = await response.Content.ReadFromJsonAsync<WeekRulesResponse>();
        body!.UsesOverride.Should().BeFalse();
        body.Rules.Should().HaveCount(1);
        body.Rules[0].RuleType.Should().Be(GameSetRuleType.Top25);
    }

    [Fact]
    public async Task GivenAWeekOverridePut_WhenGettingWeekRules_ThenUsesOverrideIsTrueWithItsOwnRules()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        var overrideBody = new WeekRulesResponse(true, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);
        using HttpResponseMessage putResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset-rules", overrideBody);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset-rules");
        WeekRulesResponse? body = await getResponse.Content.ReadFromJsonAsync<WeekRulesResponse>();
        body!.UsesOverride.Should().BeTrue();
        body.Rules.Should().HaveCount(1);
    }

    [Fact]
    public async Task GivenAnOverrideThenCleared_WhenGettingWeekRules_ThenItEchoesTheDefaultAgain()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        var overrideBody = new WeekRulesResponse(true, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);
        await client.PutAsJsonAsync($"/api/leagues/{league.Id}/weeks/7/gameset-rules", overrideBody);

        var clearBody = new WeekRulesResponse(false, []);
        using HttpResponseMessage clearResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset-rules", clearBody);
        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/leagues/{league.Id}/weeks/7/gameset-rules");
        WeekRulesResponse? body = await getResponse.Content.ReadFromJsonAsync<WeekRulesResponse>();
        body!.UsesOverride.Should().BeFalse();
    }

    [Fact]
    public async Task GivenALockedWeek_WhenPuttingWeekRules_ThenItIs409()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            var set = new Domain.GameSets.WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = 7,
                UsesOverride = false,
                GeneratedUtc = DateTime.UtcNow,
                LockedUtc = DateTime.UtcNow,
            };
            db.WeekGameSets.Add(set);
            await db.SaveChangesAsync();
        });

        var body = new WeekRulesResponse(true, [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)]);
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/weeks/7/gameset-rules", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAWeekOutsideTheLeagueRange_WhenGettingWeekRules_ThenItIs404()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{league.Id}/weeks/99/gameset-rules");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
