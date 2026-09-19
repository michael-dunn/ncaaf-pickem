using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Auth;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Display names end to end (Feature 01 / 08, P1-03 card): length bounds and per-league
/// uniqueness for both <c>PUT /api/me</c> (global) and
/// <c>PUT /api/leagues/{leagueId}/members/me/display-name</c> (per-league override), the
/// global-rename collision rule, and the <see cref="MemberNameProjection"/> helper as used by the
/// members endpoint. Was <c>DisplayNameConflictTests</c>; renamed and extended by P1-03.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class DisplayNameTests
{
    private readonly ApiTestFixture _fixture;

    public DisplayNameTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- Per-league override: value/uniqueness behaviour ------------------------------------

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
        row.IsMe.Should().BeTrue("the response is always the caller's own row");

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

    // ---- Length bounds: per-league override (0, 1, 30, 31 chars, whitespace-only) -----------

    [Fact]
    public async Task GivenAnEmptyOverride_WhenSettingIt_ThenItClearsRatherThanRejects()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an empty override clears rather than errors");
    }

    [Fact]
    public async Task GivenAWhitespaceOnlyOverride_WhenSettingIt_ThenItClearsRatherThanRejects()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("    "));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string? stored = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Memberships.AsNoTracking().SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == scenario.MemberUserId)).DisplayNameOverride);
        stored.Should().BeNull();
    }

    [Fact]
    public async Task GivenAOneCharacterOverride_WhenSettingIt_ThenItIsAccepted()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("A"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAThirtyCharacterOverride_WhenSettingIt_ThenItIsAccepted()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        string thirty = new('A', 30);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest(thirty));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAThirtyOneCharacterOverride_WhenSettingIt_ThenItIsRejected()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        string thirtyOne = new('A', 31);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest(thirtyOne));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Length bounds: global name via PUT /api/me (0, 1, 30, 31 chars, whitespace-only) ---

    [Fact]
    public async Task GivenAnEmptyGlobalName_WhenSettingIt_ThenItIsRejected()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Unchanged"));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/me", new UpdateMeRequest(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAWhitespaceOnlyGlobalName_WhenSettingIt_ThenItIsRejected()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Unchanged"));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/me", new UpdateMeRequest("    "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "trimmed to empty, same as an empty string");
    }

    [Fact]
    public async Task GivenAOneCharacterGlobalName_WhenSettingIt_ThenItIsAccepted()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/me", new UpdateMeRequest("A"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAThirtyCharacterGlobalName_WhenSettingIt_ThenItIsAccepted()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        string thirty = new('A', 30);
        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/me", new UpdateMeRequest(thirty));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAThirtyOneCharacterGlobalName_WhenSettingIt_ThenItIsRejected()
    {
        User user = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Unchanged"));
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(user.Id);

        string thirtyOne = new('A', 31);
        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/me", new UpdateMeRequest(thirtyOne));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Global-name collision rule (P1-03, DECISIONS.md) -----------------------------------

    [Fact]
    public async Task GivenAGlobalNameThatCollidesInALeague_WhenSettingIt_ThenItIs409AndUnchanged()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.PutAsJsonAsync("/api/me", new UpdateMeRequest("Commish Global"));

        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpResponseMessage response =
            await member.PutAsJsonAsync("/api/me", new UpdateMeRequest("commish global")); // case-insensitive

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        string stored = await _fixture.Factory.QueryDbAsync(async database =>
            (await database.Users.AsNoTracking().SingleAsync(u => u.Id == scenario.MemberUserId)).DisplayName);
        stored.Should().Be("Member", "the rejected rename must not be applied");
    }

    [Fact]
    public async Task GivenAGlobalNameThatWouldCollideButTheOtherMemberHasAnOverride_WhenSettingIt_ThenItSucceeds()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        // The commissioner shields their real name behind a league-only override...
        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("Skipper"));

        // ...so a member renaming their global name to "Commish" (the commissioner's global name)
        // does not collide with the commissioner's *effective* name in this league ("Skipper").
        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpResponseMessage response = await member.PutAsJsonAsync("/api/me", new UpdateMeRequest("Commish"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenTheCallersOwnOverrideShieldsThem_WhenSettingAGlobalNameThatWouldOtherwiseCollide_ThenItSucceeds()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.PutAsJsonAsync("/api/me", new UpdateMeRequest("Shared Name"));

        // The member has their own per-league override, so their global rename can't collide
        // with anyone's *effective* name in this league even if the raw string matches.
        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await member.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("Member Nickname"));

        using HttpResponseMessage response = await member.PutAsJsonAsync("/api/me", new UpdateMeRequest("Shared Name"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Projection helper used by the members endpoint --------------------------------------

    [Fact]
    public async Task GivenAMemberWithAnOverride_WhenListingMembers_ThenTheRowShowsTheOverride()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await member.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("League Nickname"));

        using HttpClient commish = _fixture.Factory.CreateClientAs(scenario.CommissionerUserId);
        MemberRow[]? rows = await commish.GetFromJsonAsync<MemberRow[]>(
            $"/api/leagues/{scenario.LeagueId}/members");

        MemberRow row = rows!.Should().ContainSingle(r => r.MembershipId != Guid.Empty
            && r.Role == NcaafPickEm.Shared.Enums.MembershipRole.Member).Subject;
        row.DisplayName.Should().Be("League Nickname");
        row.IsMe.Should().BeFalse("the commissioner is asking, not the member");
    }

    [Fact]
    public async Task GivenAMemberWithNoOverride_WhenListingMembers_ThenTheRowShowsTheGlobalName()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        using HttpClient commish = _fixture.Factory.CreateClientAs(scenario.CommissionerUserId);
        MemberRow[]? rows = await commish.GetFromJsonAsync<MemberRow[]>(
            $"/api/leagues/{scenario.LeagueId}/members");

        MemberRow row = rows!.Should().ContainSingle(r => r.DisplayName == "Member").Subject;
        row.IsMe.Should().BeFalse();

        MemberRow commishRow = rows!.Should().ContainSingle(r => r.DisplayName == "Commish").Subject;
        commishRow.IsMe.Should().BeTrue("the commissioner is asking about their own row");
    }

    [Fact]
    public async Task GivenAMembersRowAndMeResponse_WhenBothAreRead_ThenTheyAgreeOnTheEffectiveName()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        using HttpClient member = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await member.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/members/me/display-name",
            new SetLeagueDisplayNameRequest("Both Agree"));

        MemberRow[]? rows = await member.GetFromJsonAsync<MemberRow[]>($"/api/leagues/{scenario.LeagueId}/members");
        MemberRow myRow = rows!.Should().ContainSingle(r => r.IsMe).Subject;
        myRow.DisplayName.Should().Be("Both Agree");

        // MeResponse does not carry the per-league override directly (it is global-name only),
        // but the leagues it lists must be the same set the members endpoint scopes to.
        MeResponse? me = await member.GetFromJsonAsync<MeResponse>("/api/me");
        me!.Leagues.Should().ContainSingle(l => l.LeagueId == scenario.LeagueId);
    }
}
