using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>/api/leagues/*</c> except invites (Feature 01, P1-01). Uses <see cref="ApiTestFixture.PinnedFactory"/>
/// so "current week" is deterministic (2026 fixture calendar, week 7).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LeagueEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public LeagueEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<User> CreateUserAsync(string? displayName = null) =>
        await _fixture.PinnedFactory.QueryDbAsync(database => TestUsers.CreateUserAsync(database, displayName));

    [Fact]
    public async Task GivenALoggedInUser_WhenCreatingALeague_ThenDefaultsAreAppliedAndCreatorIsCommissioner()
    {
        User creator = await CreateUserAsync("Creator");
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(creator.Id);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/leagues", new CreateLeagueRequest("League Created Via Api Test", 2026, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        LeagueDetail? detail = await response.Content.ReadFromJsonAsync<LeagueDetail>();
        detail.Should().NotBeNull();
        detail!.Name.Should().Be("League Created Via Api Test");
        detail.FirstWeek.Should().Be(1);
        detail.LastWeek.Should().Be(14);
        detail.DefaultPointValue.Should().Be(10);
        detail.MyRole.Should().Be(MembershipRole.Commissioner);
        detail.CurrentWeek.Should().Be(ApiTestFixture.PinnedCurrentWeek);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenAnEmptyName_WhenCreatingALeague_ThenItIsRejected(string name)
    {
        User creator = await CreateUserAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(creator.Id);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/leagues", new CreateLeagueRequest(name, 2026, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAChampionshipWeekAsLastWeek_WhenCreatingALeague_ThenItIsRejected()
    {
        User creator = await CreateUserAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(creator.Id);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/leagues", new CreateLeagueRequest("Bad Range", 2026, 1, 15));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "week 15 is championship week, not regular season");
    }

    [Fact]
    public async Task GivenTwoLeagues_WhenListingMine_ThenBothComeBackWithMyRole()
    {
        User user = await CreateUserAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(user.Id);

        await client.PostAsJsonAsync("/api/leagues", new CreateLeagueRequest("League A", 2026, null, null));
        await client.PostAsJsonAsync("/api/leagues", new CreateLeagueRequest("League B", 2026, null, null));

        LeagueSummary[]? mine = await client.GetFromJsonAsync<LeagueSummary[]>("/api/leagues");

        mine.Should().NotBeNull();
        mine!.Should().HaveCountGreaterThanOrEqualTo(2);
        mine.Should().Contain(l => l.Name == "League A" && l.MyRole == MembershipRole.Commissioner);
        mine.Should().Contain(l => l.Name == "League B" && l.MyRole == MembershipRole.Commissioner);
    }

    [Fact]
    public async Task GivenAMember_WhenGettingLeagueDetail_ThenTheShapeIncludesCurrentWeek()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        LeagueDetail? detail = await client.GetFromJsonAsync<LeagueDetail>($"/api/leagues/{scenario.LeagueId}");

        detail.Should().NotBeNull();
        detail!.LeagueId.Should().Be(scenario.LeagueId);
        detail.MyRole.Should().Be(MembershipRole.Member);
        detail.CurrentWeek.Should().Be(ApiTestFixture.PinnedCurrentWeek);
        detail.CurrentWeekLockAtUtc.Should().BeNull("no game set exists yet for the current week");
        detail.MyCurrentWeekStatus.Should().BeNull("no game set exists yet for the current week");
    }

    [Fact]
    public async Task GivenACommissioner_WhenUpdatingSettings_ThenTheyAreSaved()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/settings",
            new UpdateLeagueSettingsRequest("Renamed", 2, 13, 25));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        LeagueDetail? detail = await response.Content.ReadFromJsonAsync<LeagueDetail>();
        detail!.Name.Should().Be("Renamed");
        detail.FirstWeek.Should().Be(2);
        detail.LastWeek.Should().Be(13);
        detail.DefaultPointValue.Should().Be(25);
    }

    [Fact]
    public async Task GivenAMemberCaller_WhenUpdatingSettings_ThenItIsForbidden()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/settings",
            new UpdateLeagueSettingsRequest("Renamed", 1, 14, 10));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GivenACommissionerCaller_WhenListingMembers_ThenStatusColumnIsPopulated()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);

        // A game set must exist for the current week before there is any submission status to
        // report; without one every caller sees null (LeagueEndpointsTests below covers that).
        await _fixture.PinnedFactory.ExecuteDbAsync(database =>
        {
            database.WeekGameSets.Add(new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = scenario.LeagueId,
                Week = ApiTestFixture.PinnedCurrentWeek,
                GeneratedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
            });
            return database.SaveChangesAsync();
        });

        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(scenario.CommissionerUserId);

        MemberRow[]? rows = await client.GetFromJsonAsync<MemberRow[]>($"/api/leagues/{scenario.LeagueId}/members");

        rows.Should().NotBeNull();
        rows!.Should().HaveCount(2);
        rows.Should().Contain(r => r.Role == MembershipRole.Commissioner && r.CurrentWeekStatus != null);
        rows.Should().Contain(r => r.Role == MembershipRole.Member && r.CurrentWeekStatus != null);
    }

    [Fact]
    public async Task GivenAMemberCaller_WhenListingMembers_ThenStatusColumnIsNull()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        MemberRow[]? rows = await client.GetFromJsonAsync<MemberRow[]>($"/api/leagues/{scenario.LeagueId}/members");

        rows.Should().NotBeNull();
        rows!.Should().OnlyContain(r => r.CurrentWeekStatus == null);
    }

    [Fact]
    public async Task GivenALeaguesFirstAndLastWeek_WhenGettingLeagueWeeks_ThenTheRangeAndCurrentAreCorrect()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        LeagueWeek[]? weeks = await client.GetFromJsonAsync<LeagueWeek[]>($"/api/leagues/{scenario.LeagueId}/weeks");

        weeks.Should().NotBeNull();
        weeks!.Select(w => w.Week).Should().Equal(Enumerable.Range(1, 14));
        weeks.Should().OnlyContain(w => !w.HasGameSet && !w.IsLocked && !w.IsComplete && w.LockAtUtc == null);
        weeks.Single(w => w.Week == ApiTestFixture.PinnedCurrentWeek).IsCurrent.Should().BeTrue();
        weeks.Count(w => w.IsCurrent).Should().Be(1);
    }
}
