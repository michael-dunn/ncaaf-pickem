using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Shared.Contracts.Dashboard;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01: the first of 05-Conventions.md's three "never" rules, proved across every surface that
/// could break it at once — no member may learn another member's pick before the week locks.
/// </summary>
/// <remarks>
/// The other two rules already have dedicated suites: <see cref="PostLockMutationTests"/> and
/// <see cref="LockEnforcementTests"/> for "no mutations after lock", and the P8-01 report's grep
/// (Domain, Infrastructure and Api are all free of <c>DateTime.UtcNow</c>) for "no clock in the
/// Domain". What was missing was one place that takes a single unlocked week and walks
/// <em>every</em> read that shows picks, which is what this class does.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class NeverRulesTests
{
    private readonly ApiTestFixture _fixture;

    public NeverRulesTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnUnlockedWeekWherePicksExist_WhenEveryReadIsWalked_ThenNoPickLeaks()
    {
        ApiFactory factory = _fixture.PinnedFactory;
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(factory);

        // Both members pick, so there is genuinely something to leak.
        using HttpClient member = factory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = factory.CreateMutatingClientAs(scenario.SecondMemberUserId);
        using HttpClient commissioner = factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        GameSetGameDto game = scenario.Games[0];
        Guid memberPick = game.HomeTeam.TeamId;
        Guid secondPick = game.AwayTeam.TeamId;

        await SetPickAsync(member, scenario, game.GameId, memberPick);
        await SetPickAsync(second, scenario, game.GameId, secondPick);

        string weekRoute = $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}";

        // 1. Everyone's picks: 403 until the lock job has settled the week.
        using (HttpResponseMessage allPicks = await member.GetAsync($"{weekRoute}/picks"))
        {
            allPicks.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await allPicks.Content.ReadAsStringAsync()).Should().Contain("PicksNotVisible");
        }

        // 2. The pick grid: same refusal, same ProblemDetails title.
        using (HttpResponseMessage grid = await member.GetAsync($"{weekRoute}/grid"))
        {
            grid.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await grid.Content.ReadAsStringAsync()).Should().Contain("PicksNotVisible");
        }

        // 3. The influence dashboard: available, but empty - it never 403s, it just has nothing
        //    to say yet (D-116).
        using (HttpResponseMessage dashboard = await member.GetAsync($"{weekRoute}/dashboard"))
        {
            dashboard.StatusCode.Should().Be(HttpStatusCode.OK);

            DashboardResponse? body = await dashboard.Content.ReadFromJsonAsync<DashboardResponse>();
            body!.IsAvailable.Should().BeFalse();
            body.Games.Should().BeEmpty();
            body.EveryoneAgrees.Should().BeEmpty();
            body.PointsSoFar.Should().Be(0);
            body.MaxRemaining.Should().Be(0);
        }

        // 4. The commissioner's roster is allowed before lock by contract - statuses only. Prove
        //    on the wire that it carries no team id at all, not just that the DTO has no field
        //    for one.
        using (HttpResponseMessage status = await commissioner.GetAsync($"{weekRoute}/picks/status"))
        {
            status.StatusCode.Should().Be(HttpStatusCode.OK);

            string json = await status.Content.ReadAsStringAsync();
            json.Should().NotContain(memberPick.ToString(), "the roster must not name anyone's pick");
            json.Should().NotContain(secondPick.ToString());
            json.Should().NotContain("teamId", "the roster carries statuses and counts only");
            json.Should().NotContain("gameSetGameId");

            MemberStatusRow[]? rows = await status.Content.ReadFromJsonAsync<MemberStatusRow[]>();
            rows.Should().Contain(row => row.MembershipId == scenario.MemberMembershipId);
        }

        // 5. A member's own picks read still shows only their own choice.
        using (HttpResponseMessage mine = await second.GetAsync($"{weekRoute}/picks/me"))
        {
            mine.StatusCode.Should().Be(HttpStatusCode.OK);

            MyPicksResponse? body = await mine.Content.ReadFromJsonAsync<MyPicksResponse>();
            MyPickGameDto row = body!.Games.Single(entry => entry.Game.GameId == game.GameId);

            row.MyTeamId.Should().Be(secondPick);

            string json = await mine.Content.ReadAsStringAsync();
            json.Should().NotContain(
                scenario.MemberMembershipId.ToString(),
                "another member must not appear in a personal picks read");
        }
    }

    private static async Task SetPickAsync(
        HttpClient client,
        PickWeekScenario scenario,
        Guid gameId,
        Guid teamId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{gameId}",
            new SetPickRequest(teamId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
