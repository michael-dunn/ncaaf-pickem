using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET /api/invites/{code}</c> and <c>POST /api/invites/{code}/accept</c> (Feature 01, P1-01).
/// Uses <see cref="ApiTestFixture.PinnedFactory"/> so <c>JoinedWeek</c> is deterministic.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class InviteAcceptTests
{
    private readonly ApiTestFixture _fixture;

    public InviteAcceptTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(LeagueScenario Scenario, InviteResponse Invite)> CreateLeagueWithInviteAsync()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient commish = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        InviteResponse? invite = await (await commish.PostAsync($"/api/leagues/{scenario.LeagueId}/invites", null))
            .Content.ReadFromJsonAsync<InviteResponse>();

        return (scenario, invite!);
    }

    [Fact]
    public async Task GivenAValidInvite_WhenAccepted_ThenTheCallerJoinsAtTheCurrentWeekAndUsesIncrement()
    {
        (LeagueScenario scenario, InviteResponse invite) = await CreateLeagueWithInviteAsync();

        User joiner = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, "Joiner"));
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(joiner.Id);

        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        LeagueDetail? detail = await response.Content.ReadFromJsonAsync<LeagueDetail>();
        detail!.LeagueId.Should().Be(scenario.LeagueId);

        Membership membership = await _fixture.PinnedFactory.QueryDbAsync(database =>
            database.Memberships.AsNoTracking().SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == joiner.Id));

        membership.JoinedWeek.Should().Be(ApiTestFixture.PinnedCurrentWeek);
        membership.RemovedUtc.Should().BeNull();

        int uses = await _fixture.PinnedFactory.QueryDbAsync(async database =>
            (await database.Invites.AsNoTracking().SingleAsync(i => i.Id == invite.InviteId)).Uses);
        uses.Should().Be(1);
    }

    [Fact]
    public async Task GivenAnExpiredInvite_WhenAcceptAttempted_ThenItIs409WithExpiredState()
    {
        (_, InviteResponse invite) = await CreateLeagueWithInviteAsync();

        await _fixture.PinnedFactory.ExecuteDbAsync(async database =>
        {
            Invite tracked = await database.Invites.SingleAsync(i => i.Id == invite.InviteId);
            tracked.ExpiresUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime.AddDays(-1);
            await database.SaveChangesAsync();
        });

        User joiner = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(joiner.Id);

        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        InvitePreview? preview = await response.Content.ReadFromJsonAsync<InvitePreview>();
        preview!.State.Should().Be(InviteState.Expired);
    }

    [Fact]
    public async Task GivenARevokedInvite_WhenAcceptAttempted_ThenItIs409WithRevokedState()
    {
        (LeagueScenario scenario, InviteResponse invite) = await CreateLeagueWithInviteAsync();

        using HttpClient commish = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage revoke = await commish.DeleteAsync(
            $"/api/leagues/{scenario.LeagueId}/invites/{invite.InviteId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        User joiner = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(joiner.Id);

        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        InvitePreview? preview = await response.Content.ReadFromJsonAsync<InvitePreview>();
        preview!.State.Should().Be(InviteState.Revoked);
    }

    [Fact]
    public async Task GivenAnInviteWithNoUsesLeft_WhenAcceptAttempted_ThenItIs409WithFullState()
    {
        (_, InviteResponse invite) = await CreateLeagueWithInviteAsync();

        await _fixture.PinnedFactory.ExecuteDbAsync(async database =>
        {
            Invite tracked = await database.Invites.SingleAsync(i => i.Id == invite.InviteId);
            tracked.MaxUses = 1;
            tracked.Uses = 1;
            await database.SaveChangesAsync();
        });

        User joiner = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(joiner.Id);

        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        InvitePreview? preview = await response.Content.ReadFromJsonAsync<InvitePreview>();
        preview!.State.Should().Be(InviteState.Full);
    }

    [Fact]
    public async Task GivenACallerAlreadyAMember_WhenAcceptAttempted_ThenItIs409WithAlreadyMemberState()
    {
        (LeagueScenario scenario, InviteResponse invite) = await CreateLeagueWithInviteAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        InvitePreview? preview = await response.Content.ReadFromJsonAsync<InvitePreview>();
        preview!.State.Should().Be(InviteState.AlreadyMember);
    }

    [Fact]
    public async Task GivenAValidInvite_WhenPreviewed_ThenItShowsTheLeagueAndValidState()
    {
        (LeagueScenario scenario, InviteResponse invite) = await CreateLeagueWithInviteAsync();
        _ = scenario;

        User caller = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(caller.Id);

        InvitePreview? preview = await client.GetFromJsonAsync<InvitePreview>($"/api/invites/{invite.Code}");

        preview.Should().NotBeNull();
        preview!.State.Should().Be(InviteState.Valid);
        preview.SeasonYear.Should().Be(2026);
        preview.MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task GivenAFormerMember_WhenAcceptingANewInvite_ThenTheirMembershipIsReactivated()
    {
        (LeagueScenario scenario, InviteResponse invite) = await CreateLeagueWithInviteAsync();

        await _fixture.PinnedFactory.ExecuteDbAsync(async database =>
        {
            Membership membership = await database.Memberships.SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == scenario.MemberUserId);
            membership.RemovedUtc = DateTime.UtcNow;

            // Seeded as a commissioner so the Role reset D-037 asks for is actually asserted
            // below rather than being true by accident.
            membership.Role = MembershipRole.Commissioner;
            await database.SaveChangesAsync();
        });

        // Removed, so a fresh invite must succeed for them (not AlreadyMember).
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        Membership reactivated = await _fixture.PinnedFactory.QueryDbAsync(database =>
            database.Memberships.AsNoTracking().SingleAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == scenario.MemberUserId));

        reactivated.RemovedUtc.Should().BeNull();
        reactivated.JoinedWeek.Should().Be(ApiTestFixture.PinnedCurrentWeek);
        reactivated.Role.Should().Be(MembershipRole.Member, "a reactivated membership starts over as a plain member (D-037)");
    }

    [Fact]
    public async Task GivenAnUnknownCode_WhenPreviewed_ThenItIs404()
    {
        User caller = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(caller.Id);

        using HttpResponseMessage response = await client.GetAsync("/api/invites/NOSUCHCD");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnknownCode_WhenAcceptAttempted_ThenItIs404()
    {
        User caller = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(caller.Id);

        using HttpResponseMessage response = await client.PostAsync("/api/invites/NOSUCHCD/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenALeagueAtTheMemberCap_WhenAcceptAttempted_ThenItIs409WithFullState()
    {
        (LeagueScenario scenario, InviteResponse invite) = await CreateLeagueWithInviteAsync();

        // The scenario league already has 2 active members; top it up to the 50-member cap.
        await _fixture.PinnedFactory.ExecuteDbAsync(async database =>
        {
            League league = await database.Leagues.SingleAsync(l => l.Id == scenario.LeagueId);
            for (int i = 0; i < LeagueRules.MemberCap - 2; i++)
            {
                User filler = await TestUsers.CreateUserAsync(database);
                await TestUsers.CreateMembershipAsync(database, league, filler);
            }
        });

        User joiner = await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(joiner.Id);

        using HttpResponseMessage response = await client.PostAsync($"/api/invites/{invite.Code}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        InvitePreview? preview = await response.Content.ReadFromJsonAsync<InvitePreview>();
        preview!.State.Should().Be(InviteState.Full);
        preview.MemberCount.Should().Be(LeagueRules.MemberCap);

        bool joined = await _fixture.PinnedFactory.QueryDbAsync(database =>
            database.Memberships.AsNoTracking().AnyAsync(
                m => m.LeagueId == scenario.LeagueId && m.UserId == joiner.Id));
        joined.Should().BeFalse("a full league must not add the caller");
    }
}
