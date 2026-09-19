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
    public async Task GivenAMissingName_WhenCreatingALeague_ThenItIs400NotAServerError()
    {
        User creator = await CreateUserAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(creator.Id);

        // Name omitted entirely: the record's non-nullable string binds to null at runtime, which
        // the validator must reject rather than dereference.
        using var body = new StringContent(
            """{"seasonYear":2026}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/leagues", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a missing name is a validation error, not a 500");
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
    public async Task GivenASeasonWithNoCalendarAtAll_WhenCreatingALeague_ThenItSucceedsWithTheDefaultWeeks()
    {
        // 2031 has no calendar from any source, which is also the state of a freshly deployed
        // instance before the reference-data bootstrap lands (P8-06, D-165): the create-league
        // page promises weeks 1..14 there, so the API must not refuse the request.
        User creator = await CreateUserAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(creator.Id);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/leagues", new CreateLeagueRequest("League Before The Calendar", 2031, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        LeagueDetail? detail = await response.Content.ReadFromJsonAsync<LeagueDetail>();
        detail.Should().NotBeNull();
        detail!.SeasonYear.Should().Be(2031);
        detail.FirstWeek.Should().Be(1);
        detail.LastWeek.Should().Be(14);
        detail.CurrentWeek.Should().Be(1, "with no calendar the league's first week stands in");
        detail.MyRole.Should().Be(MembershipRole.Commissioner);
    }

    [Fact]
    public async Task GivenASeasonWithACalendar_WhenCreatingALeagueOutsideIt_ThenItIsStillRejected()
    {
        // The other half of D-165: the fallback applies only when there is nothing to validate
        // against. 2026 has a calendar, so week 20 is still a bad request.
        User creator = await CreateUserAsync();
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(creator.Id);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/leagues", new CreateLeagueRequest("Out Of Range", 2026, 1, 20));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
    public async Task GivenAMissingName_WhenUpdatingSettings_ThenItIs400NotAServerError()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using var body = new StringContent(
            """{"firstWeek":1,"lastWeek":14,"defaultPointValue":10}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PutAsync(
            $"/api/leagues/{scenario.LeagueId}/settings", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    // ---- P1-01 review follow-ups: InvalidPointValue (400) and MembershipNotFound (404) ------

    [Fact]
    public async Task GivenADefaultPointValueOutOfRange_WhenUpdatingSettings_ThenItIs400NotAServerError()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/settings",
            new UpdateLeagueSettingsRequest("Renamed", 1, 14, 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnknownMembershipId_WhenRemoving_ThenItIs404()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response =
            await client.DeleteAsync($"/api/leagues/{scenario.LeagueId}/members/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnknownMembershipId_WhenPromoting_ThenItIs404()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{Guid.CreateVersion7()}/promote", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnknownMembershipId_WhenDemoting_ThenItIs404()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/members/{Guid.CreateVersion7()}/demote", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnknownMembershipId_WhenTransferringCommissioner_ThenItIs404()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.PinnedFactory);
        using HttpClient client = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/commissioner/transfer",
            new TransferRequest(Guid.CreateVersion7()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
