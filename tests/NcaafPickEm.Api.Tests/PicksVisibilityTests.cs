using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Who may see whose picks (Feature 04, Visibility): <c>GET .../picks</c> is 403 until the week
/// locks, and <c>GET .../picks/status</c> is the commissioner's alone.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class PicksVisibilityTests
{
    private readonly ApiTestFixture _fixture;

    public PicksVisibilityTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnUnlockedWeek_WhenAMemberAsksForEveryonesPicks_ThenItIs403()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.GetAsync(scenario.PicksRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PicksNotVisible");
    }

    [Fact]
    public async Task GivenALockedWeek_WhenAMemberAsksForEveryonesPicks_ThenEveryPickIsVisible()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);

        GameSetGameDto first = scenario.Games[0];
        await PickAsync(member, scenario, first, first.HomeTeam.TeamId);
        await PickAsync(second, scenario, first, first.AwayTeam.TeamId);

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpResponseMessage response = await member.GetAsync(scenario.PicksRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekPicksResponse? body = await response.Content.ReadFromJsonAsync<WeekPicksResponse>();

        body!.Games.Should().HaveCount(scenario.Games.Length);
        body.Members.Should().HaveCount(2);

        MemberPicksRow mine = body.Members.Single(row => row.MembershipId == scenario.MemberMembershipId);
        MemberPicksRow theirs = body.Members.Single(row => row.MembershipId == scenario.SecondMemberMembershipId);

        mine.Picks.Single(pick => pick.GameSetGameId == first.GameSetGameId).TeamId
            .Should().Be(first.HomeTeam.TeamId);
        theirs.Picks.Single(pick => pick.GameSetGameId == first.GameSetGameId).TeamId
            .Should().Be(first.AwayTeam.TeamId);

        // Every member carries one entry per active game, null where they never picked.
        mine.Picks.Should().HaveCount(scenario.Games.Length);
        mine.Picks.Count(pick => pick.TeamId is null).Should().Be(scenario.Games.Length - 1);
    }

    [Fact]
    public async Task GivenAMemberWhoLeftAfterTheWeekLocked_WhenReadingEveryonesPicks_ThenTheyStillAppear()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);

        GameSetGameDto first = scenario.Games[0];
        await PickAsync(member, scenario, first, first.HomeTeam.TeamId);
        await PickAsync(second, scenario, first, first.AwayTeam.TeamId);

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        await RemoveMemberAsync(scenario.SecondMemberMembershipId);

        using HttpResponseMessage response = await member.GetAsync(scenario.PicksRoute);
        WeekPicksResponse? body = await response.Content.ReadFromJsonAsync<WeekPicksResponse>();

        body!.Members.Should().Contain(row => row.MembershipId == scenario.SecondMemberMembershipId,
            "their picks were part of the week that locked");
    }

    [Fact]
    public async Task GivenAMemberWhoPickedThenLeftBeforeLock_WhenReadingEveryonesPicks_ThenTheyHaveNoColumn()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);

        GameSetGameDto first = scenario.Games[0];
        await PickAsync(member, scenario, first, first.HomeTeam.TeamId);
        await PickAsync(second, scenario, first, first.AwayTeam.TeamId);

        // Removed *before* the lock, so the lock job never settles their row (D-111) and it is left
        // behind at InProgress. "Has a WeekSubmissions row" would still name them; the roster rule
        // is the status the job writes (D-135).
        await RemoveMemberAsync(scenario.SecondMemberMembershipId);

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpResponseMessage response = await member.GetAsync(scenario.PicksRoute);
        WeekPicksResponse? body = await response.Content.ReadFromJsonAsync<WeekPicksResponse>();

        body!.Members.Should().NotContain(row => row.MembershipId == scenario.SecondMemberMembershipId);
        body.Members.Should().ContainSingle(row => row.MembershipId == scenario.MemberMembershipId);
    }

    [Fact]
    public async Task GivenTheStatusRoster_WhenAPlainMemberAsks_ThenItIs403()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync($"{scenario.PicksRoute}/status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GivenTheStatusRoster_WhenTheCommissionerAsks_ThenEveryActiveMemberHasARow()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient commish = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        await PickAsync(member, scenario, scenario.Games[0], scenario.Games[0].HomeTeam.TeamId);

        using HttpResponseMessage response = await commish.GetAsync($"{scenario.PicksRoute}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MemberStatusRow[]? rows = await response.Content.ReadFromJsonAsync<MemberStatusRow[]>();

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.TotalCount == scenario.Games.Length);

        MemberStatusRow picked = rows!.Single(row => row.MembershipId == scenario.MemberMembershipId);
        picked.Status.Should().Be(SubmissionStatus.InProgress);
        picked.PickedCount.Should().Be(1);

        MemberStatusRow untouched = rows!.Single(row => row.MembershipId == scenario.SecondMemberMembershipId);
        untouched.Status.Should().Be(SubmissionStatus.NotStarted);
        untouched.PickedCount.Should().Be(0);
    }

    [Fact]
    public async Task GivenAWeekWithNoGameSet_WhenTheCommissionerAsksForTheRoster_ThenEveryoneIsNotStarted()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient commish = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await commish.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/6/picks/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MemberStatusRow[]? rows = await response.Content.ReadFromJsonAsync<MemberStatusRow[]>();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Status == SubmissionStatus.NotStarted && row.TotalCount == 0);
    }

    [Fact]
    public async Task GivenAFinishedGameInALockedWeek_WhenAMemberReadsTheirOwnPicks_ThenTheWinnerIsCarried()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);

        GameSetGameDto first = scenario.Games[0];
        await PickAsync(member, scenario, first, first.HomeTeam.TeamId);

        // A commissioner result override, not a score on the shared fixture Game row: the Games
        // table is common to every league in the run's one database (AGENT-NOTES, "Game sets and
        // points"), while this row belongs to this league alone.
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame row = await db.WeekGameSetGames.SingleAsync(
                candidate => candidate.Id == first.GameSetGameId!.Value);
            row.ResultOverrideWinnerTeamId = first.HomeTeam.TeamId;
            await db.SaveChangesAsync();
        });

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpResponseMessage response = await member.GetAsync($"{scenario.PicksRoute}/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MyPicksResponse? body = await response.Content.ReadFromJsonAsync<MyPicksResponse>();
        body!.IsLocked.Should().BeTrue();

        MyPickGameDto pickRow = body.Games.Single(candidate => candidate.Game.GameId == first.GameId);
        pickRow.MyTeamId.Should().Be(first.HomeTeam.TeamId);
        pickRow.Game.WinnerTeamId.Should().Be(first.HomeTeam.TeamId, "the picks page colours a past week from WinnerTeamId");
    }

    private async Task RemoveMemberAsync(Guid membershipId) =>
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            Membership leaving = await db.Memberships.SingleAsync(m => m.Id == membershipId);
            leaving.RemovedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime;
            await db.SaveChangesAsync();
        });

    private static async Task PickAsync(
        HttpClient client,
        PickWeekScenario scenario,
        GameSetGameDto game,
        Guid teamId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(teamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
