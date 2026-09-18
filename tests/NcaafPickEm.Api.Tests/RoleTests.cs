using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Promote, demote, transfer, and remove (Feature 01, P1-01).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class RoleTests
{
    private readonly ApiTestFixture _fixture;

    public RoleTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Membership> GetMembershipAsync(Guid leagueId, Guid userId) =>
        await _fixture.Factory.QueryDbAsync(database =>
            database.Memberships.AsNoTracking().SingleAsync(m => m.LeagueId == leagueId && m.UserId == userId));

    [Fact]
    public async Task GivenACommissioner_WhenPromotingAMember_ThenTheyBecomeCommissioner()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership member = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await commish.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{member.Id}/promote", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Membership updated = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);
        updated.Role.Should().Be(MembershipRole.Commissioner);
    }

    [Fact]
    public async Task GivenTwoCommissioners_WhenDemotingOne_ThenTheOtherRemainsCommissioner()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership member = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.PostAsync($"/api/leagues/{scenario.LeagueId}/members/{member.Id}/promote", null);

        using HttpResponseMessage response = await commish.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{member.Id}/demote", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Membership updatedMember = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);
        Membership updatedCommish = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);
        updatedMember.Role.Should().Be(MembershipRole.Member);
        updatedCommish.Role.Should().Be(MembershipRole.Commissioner);
    }

    [Fact]
    public async Task GivenTheOnlyCommissioner_WhenDemotingThemself_ThenItIs409()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership commissioner = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await commish.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{commissioner.Id}/demote", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        Membership stillCommissioner = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);
        stillCommissioner.Role.Should().Be(MembershipRole.Commissioner);
    }

    [Fact]
    public async Task GivenATransfer_WhenApplied_ThenTargetIsPromotedCallerIsDemotedAndOthersAreUntouched()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership member = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        // A third commissioner who must stay untouched by the transfer.
        User thirdUser = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Third"));
        League league = await _fixture.Factory.QueryDbAsync(database =>
            database.Leagues.AsNoTracking().SingleAsync(l => l.Id == scenario.LeagueId));
        await _fixture.Factory.ExecuteDbAsync(database =>
            TestUsers.CreateMembershipAsync(database, league, thirdUser, MembershipRole.Commissioner));

        using HttpResponseMessage response = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/commissioner/transfer",
            new TransferRequest(member.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Membership updatedCaller = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);
        Membership updatedTarget = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);
        Membership third = await GetMembershipAsync(scenario.LeagueId, thirdUser.Id);

        updatedCaller.Role.Should().Be(MembershipRole.Member);
        updatedTarget.Role.Should().Be(MembershipRole.Commissioner);
        third.Role.Should().Be(MembershipRole.Commissioner);
    }

    [Fact]
    public async Task GivenTheOnlyCommissioner_WhenRemovingThemself_ThenItIs409AsTheLastCommissioner()
    {
        // In a single-commissioner league, "remove self" and "remove the last commissioner" are
        // the same call; EnsureNotActingOnSelf is checked first so this is the code path hit,
        // but the outcome the card asks for (409, league keeps its commissioner) is the same one
        // a distinct "remove the last commissioner via someone else" case would produce - and no
        // such case can exist, because only a commissioner may call this route, so removing the
        // sole other commissioner always leaves the caller behind (see the transfer test above
        // for the "other commissioners untouched" half of that guarantee).
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership commissioner = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{commissioner.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        Membership stillActive = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);
        stillActive.RemovedUtc.Should().BeNull();
    }

    [Fact]
    public async Task GivenTwoCommissioners_WhenOneRemovesTheOther_ThenTheRemainingOneStaysCommissioner()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership member = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.PostAsync($"/api/leagues/{scenario.LeagueId}/members/{member.Id}/promote", null);

        using HttpResponseMessage response = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Membership originalCommissioner = await GetMembershipAsync(scenario.LeagueId, scenario.CommissionerUserId);
        originalCommissioner.Role.Should().Be(MembershipRole.Commissioner);
        originalCommissioner.RemovedUtc.Should().BeNull();
    }

    [Fact]
    public async Task GivenACommissioner_WhenRemovingAMember_ThenTheyAreSoftDeletedAndAuditLogged()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership member = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Membership removed = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);
        removed.RemovedUtc.Should().NotBeNull();

        bool hasAuditRow = await _fixture.Factory.QueryDbAsync(database =>
            database.AuditLog.AsNoTracking().AnyAsync(
                entry => entry.LeagueId == scenario.LeagueId
                    && entry.Action == AuditAction.MemberRemoved
                    && entry.TargetId == member.Id));

        hasAuditRow.Should().BeTrue();
    }

    [Fact]
    public async Task GivenARemovedMember_WhenListingMembers_ThenTheyAreFlaggedFormer()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        Membership member = await GetMembershipAsync(scenario.LeagueId, scenario.MemberUserId);

        using HttpClient commish = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        await commish.DeleteAsync($"/api/leagues/{scenario.LeagueId}/members/{member.Id}");

        MemberRow[]? rows = await commish.GetFromJsonAsync<MemberRow[]>($"/api/leagues/{scenario.LeagueId}/members");

        rows.Should().Contain(r => r.MembershipId == member.Id && r.IsFormer);
    }
}
