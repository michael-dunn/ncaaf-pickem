using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// D-082 (Feature 02, P3-04): <c>PUT .../weeks/{week}/gameset-rules</c> also regenerates the week
/// when it saves an override for the league's *current* week, so a commissioner editing the
/// current week's rules sees the effect immediately instead of an empty-looking week until they
/// remember to press "generate" too. Driven against <see cref="ApiTestFixture.PinnedFactory"/> so
/// week 7 is deterministically current.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class WeekOverrideRegenerateTests
{
    private readonly ApiTestFixture _fixture;

    public WeekOverrideRegenerateTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(League League, HttpClient Client)> CreateCommishLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.PinnedFactory);

        User commissioner = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "OverrideCommish"));
        League league = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(commissioner.Id);
        return (league, client);
    }

    private static GameSetRuleDto[] Top25Rule() =>
        [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];

    private async Task<WeekGameSetResponse> GetWeekAsync(HttpClient client, Guid leagueId, int week)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{leagueId}/weeks/{week}/gameset");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WeekGameSetResponse>())!;
    }

    [Fact]
    public async Task GivenTheCurrentWeek_WhenAnOverrideIsSaved_ThenTheSetIsRegeneratedInTheSameCall()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        WeekGameSetResponse before = await GetWeekAsync(client, league.Id, 7);
        before.Games.Should().BeEmpty("nothing has been generated yet");

        using HttpResponseMessage putResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset-rules",
            new WeekRulesResponse(true, Top25Rule()));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        WeekGameSetResponse after = await GetWeekAsync(client, league.Id, 7);
        after.Games.Should().NotBeEmpty("saving the current week's override should regenerate it in the same request");
    }

    [Fact]
    public async Task GivenTheCurrentWeekAlreadyRegeneratedOnSave_WhenGenerateIsCalledAgain_ThenNothingChanges()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        using HttpResponseMessage putResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset-rules",
            new WeekRulesResponse(true, Top25Rule()));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        WeekGameSetResponse afterSave = await GetWeekAsync(client, league.Id, 7);

        // The commissioner UI still calls generate right after PUT (D-060's fakes model both
        // calls); that must be an idempotent no-op now that the save already regenerated.
        using HttpResponseMessage generateResponse = await client.PostAsync(
            $"/api/leagues/{league.Id}/weeks/7/gameset/generate", null);
        generateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGameSetResponse afterGenerate = (await generateResponse.Content.ReadFromJsonAsync<WeekGameSetResponse>())!;

        afterGenerate.Games.Select(g => g.GameSetGameId).Should().BeEquivalentTo(afterSave.Games.Select(g => g.GameSetGameId));
        afterGenerate.LockAtUtc.Should().Be(afterSave.LockAtUtc);
    }

    [Fact]
    public async Task GivenANonCurrentWeek_WhenAnOverrideIsSaved_ThenItIsStillSaveOnly()
    {
        (League league, HttpClient client) = await CreateCommishLeagueAsync();

        using HttpResponseMessage putResponse = await client.PutAsJsonAsync(
            $"/api/leagues/{league.Id}/weeks/8/gameset-rules",
            new WeekRulesResponse(true, Top25Rule()));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        WeekGameSetResponse week8 = await GetWeekAsync(client, league.Id, 8);
        week8.Games.Should().BeEmpty("week 8 is not current, so D-065's save-only rule still applies");
    }
}
