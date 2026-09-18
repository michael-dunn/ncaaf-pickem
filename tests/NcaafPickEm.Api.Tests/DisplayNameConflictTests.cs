using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>PUT /api/leagues/{leagueId}/members/me/display-name</c> (Feature 01 / 08). Length bounds and
/// per-league uniqueness here; P1-03 extends this class with more of the projection behaviour.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class DisplayNameConflictTests
{
    private readonly ApiTestFixture _fixture;

    public DisplayNameConflictTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAUniqueName_WhenSettingIt_ThenItIsStoredAndReturnedAsTheEffectiveName()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("  Nickname  "));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        MemberRow? row = await response.Content.ReadFromJsonAsync<MemberRow>();
        row!.DisplayName.Should().Be("Nickname");

        string? stored = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Memberships.AsNoTracking().SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == scenario.MemberUserId)).DisplayNameOverride);
        stored.Should().Be("Nickname");
    }

    [Fact]
    public async Task GivenANameAlreadyUsedByAnotherActiveMember_WhenSettingIt_ThenItIs409()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("Taken"));

        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpResponseMessage response = await member.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("taken")); // case-insensitive collision

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenARemovedMembersName_WhenSettingIt_ThenItIsNotAConflict()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        // The member holds "FreedUp"...
        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await member.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("FreedUp"));

        // ...then the commissioner removes them, which must free the name up...
        Guid memberMembershipId = await MembershipIdAsync(scenario, scenario.MemberUserId);
        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.DeleteAsync($"/api/leagues/{scenario.LeagueId}/members/{memberMembershipId}");

        // ...for a third, still-active member.
        League league = await _fixture.Factory.QueryDbAsync(database =>
            database.Leagues.AsNoTracking().SingleAsync(l => l.Id == scenario.LeagueId));
        User third = await _fixture.Factory.QueryDbAsync(database =>
            TestUsers.CreateUserAsync(database, "Third"));
        await _fixture.Factory.ExecuteDbAsync(database =>
            TestUsers.CreateMembershipAsync(database, league, third));

        using HttpClient thirdClient = _fixture.Factory.CreateMutatingClientAs(third.Id);
        using HttpResponseMessage response = await thirdClient.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("FreedUp"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> MembershipIdAsync(LeagueScenario scenario, Guid userId) =>
        await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Memberships.AsNoTracking().SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == userId)).Id);

    [Theory]
    [InlineData("This display name is far too long to fit here")]
    public async Task GivenATooLongName_WhenSettingIt_ThenItIsRejected(string tooLong)
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest(tooLong));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenANullName_WhenSettingIt_ThenTheOverrideIsCleared()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("Temporary"));

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest(null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string? stored = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Memberships.AsNoTracking().SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == scenario.MemberUserId)).DisplayNameOverride);
        stored.Should().BeNull();
    }
}
