using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Shared.Contracts.Dashboard;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The influence dashboard endpoint (Feature 05, P6-02): unavailable before lock, the Overview
/// worked example after it, the stale-scores banner, scoring outcomes, and who the calculator
/// includes.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class DashboardTests
{
    private readonly ApiTestFixture _fixture;

    public DashboardTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAWeekWithNoGameSet_WhenAMemberAsksForTheDashboard_ThenItIsUnavailableWithNoLockTime()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient member = _fixture.Factory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/1/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        body!.IsAvailable.Should().BeFalse();
        body.LockAtUtc.Should().BeNull();
        body.LockAtEasternDisplay.Should().BeNull();
        body.PointsSoFar.Should().Be(0);
        body.MaxRemaining.Should().Be(0);
        body.Games.Should().BeEmpty();
        body.EveryoneAgrees.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenAGeneratedSetPastItsLockInstant_WhenTheJobHasNotRun_ThenTheDashboardIsStillUnavailable()
    {
        // Dashboard "locked" means WeekGameSets.LockedUtc != null - the lock job has actually run
        // - not merely that LockAtUtc has passed (unlike PickService's own guard). See
        // DashboardService's remarks and DECISIONS.md.
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        body!.IsAvailable.Should().BeFalse();
        body.LockAtUtc.Should().NotBeNull();
        body.LockAtEasternDisplay.Should().NotBeNull();
        body.Games.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenTheOverviewExample_WhenDanceAsksForHerDashboard_ThenItMatchesTheWorkedExample()
    {
        DashboardWeekScenario scenario = await DashboardWeekScenario.CreateAsync(_fixture.PinnedFactory);
        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpClient dance = _fixture.PinnedFactory.CreateClientAs(scenario.UserIdByName["Dance"]);
        using HttpResponseMessage response = await dance.GetAsync(scenario.DashboardRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        body!.IsAvailable.Should().BeTrue();
        body.EveryoneAgrees.Should().BeEmpty("Dance disagrees with the room on both games");
        body.Games.Should().HaveCount(2);

        DashboardGameDto michiganTexas = body.Games.Single(g => g.Game.GameId == scenario.MichiganTexas.GameId);
        michiganTexas.MyTeamId.Should().Be(scenario.MichiganTexas.AwayTeam.TeamId, "Dance picked Texas");
        michiganTexas.MyOutcome.Should().Be(InfluenceOutcome.Pending);
        michiganTexas.OppositeCount.Should().Be(4);
        michiganTexas.OppositePicks.Select(m => m.MembershipId).Should().BeEquivalentTo(
        [
            scenario.MembershipIdByName["Michael"],
            scenario.MembershipIdByName["Alyson"],
            scenario.MembershipIdByName["Alex"],
            scenario.MembershipIdByName["Daniel"],
        ]);
        michiganTexas.NoPick.Should().BeEmpty();

        DashboardGameDto marylandRutgers = body.Games.Single(g => g.Game.GameId == scenario.MarylandRutgers.GameId);
        marylandRutgers.MyTeamId.Should().Be(scenario.MarylandRutgers.HomeTeam.TeamId, "Dance picked Maryland");
        marylandRutgers.OppositeCount.Should().Be(1);
        marylandRutgers.OppositePicks.Select(m => m.MembershipId).Should().BeEquivalentTo(
        [
            scenario.MembershipIdByName["Alex"],
        ]);

        // The most-opposed game leads (04-Domain-Algorithms.md section 6's ordering).
        body.Games[0].Game.GameId.Should().Be(scenario.MichiganTexas.GameId);
        body.Games[1].Game.GameId.Should().Be(scenario.MarylandRutgers.GameId);
    }

    [Fact]
    public async Task GivenTheOverviewExample_WhenAlysonAsksForHerDashboard_ThenItMatchesTheWorkedExample()
    {
        DashboardWeekScenario scenario = await DashboardWeekScenario.CreateAsync(_fixture.PinnedFactory);
        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpClient alyson = _fixture.PinnedFactory.CreateClientAs(scenario.UserIdByName["Alyson"]);
        using HttpResponseMessage response = await alyson.GetAsync(scenario.DashboardRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        body!.IsAvailable.Should().BeTrue();
        body.EveryoneAgrees.Should().BeEmpty("Alyson has one dissenter on each game");
        body.Games.Should().HaveCount(2);

        DashboardGameDto michiganTexas = body.Games.Single(g => g.Game.GameId == scenario.MichiganTexas.GameId);
        michiganTexas.MyTeamId.Should().Be(scenario.MichiganTexas.HomeTeam.TeamId, "Alyson picked Michigan");
        michiganTexas.OppositeCount.Should().Be(1);
        michiganTexas.OppositePicks.Select(m => m.MembershipId).Should().BeEquivalentTo(
        [
            scenario.MembershipIdByName["Dance"],
        ]);

        DashboardGameDto marylandRutgers = body.Games.Single(g => g.Game.GameId == scenario.MarylandRutgers.GameId);
        marylandRutgers.MyTeamId.Should().Be(scenario.MarylandRutgers.HomeTeam.TeamId, "Alyson picked Maryland");
        marylandRutgers.OppositeCount.Should().Be(1);
        marylandRutgers.OppositePicks.Select(m => m.MembershipId).Should().BeEquivalentTo(
        [
            scenario.MembershipIdByName["Alex"],
        ]);

        // Both games carry one opposite pick and the same point value; kickoff order settles the
        // tie, and Maryland/Rutgers (16:00 UTC) kicks before Michigan/Texas (19:30 UTC) in the
        // fixture schedule (04-Domain-Algorithms.md section 6's ordering).
        body.Games[0].Game.GameId.Should().Be(scenario.MarylandRutgers.GameId);
        body.Games[1].Game.GameId.Should().Be(scenario.MichiganTexas.GameId);
    }

    [Fact]
    public async Task GivenAStaleLiveScoreSource_WhenAMemberAsksForTheDashboard_ThenTheFlagPropagates()
    {
        var health = new FakeLiveScoreHealth { ScoresMayBeStale = true };

        await using var factory = new ApiFactory(
            _fixture.Database.ConnectionString,
            timeProvider: new FixedTimeProvider(ApiTestFixture.PinnedNowUtc),
            configureServices: services =>
            {
                services.RemoveAll<ILiveScoreHealth>();
                services.AddSingleton<ILiveScoreHealth>(health);
            });

        DashboardWeekScenario scenario = await DashboardWeekScenario.CreateAsync(factory);
        await scenario.MarkLockedAsync(factory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpClient michael = factory.CreateClientAs(scenario.UserIdByName["Michael"]);
        using HttpResponseMessage response = await michael.GetAsync(scenario.DashboardRoute);

        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        body!.ScoresMayBeStale.Should().BeTrue();

        health.ScoresMayBeStale = false;

        using HttpResponseMessage secondResponse = await michael.GetAsync(scenario.DashboardRoute);
        DashboardResponse? secondBody = await secondResponse.Content.ReadFromJsonAsync<DashboardResponse>();
        secondBody!.ScoresMayBeStale.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAFinalGameWithAWinner_WhenTheViewerPickedIt_ThenWonAndPointsSoFarUpdate()
    {
        DashboardWeekScenario scenario = await DashboardWeekScenario.CreateAsync(_fixture.PinnedFactory);
        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        // Michigan (home) wins; Alyson picked Michigan on this game. The Games row is shared by
        // every league in the run's one database, so it must be restored afterward or later
        // tests in the same run see it as already Final (AGENT-NOTES.md, "Game sets and points").
        try
        {
            await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
            {
                Game game = await db.Games.SingleAsync(candidate => candidate.Id == scenario.MichiganTexas.GameId);
                game.Status = GameStatus.Final;
                game.HomeScore = 31;
                game.AwayScore = 17;
                await db.SaveChangesAsync();
            });

            using HttpClient alyson = _fixture.PinnedFactory.CreateClientAs(scenario.UserIdByName["Alyson"]);
            using HttpResponseMessage response = await alyson.GetAsync(scenario.DashboardRoute);
            DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

            DashboardGameDto michiganTexas = body!.Games.Single(g => g.Game.GameId == scenario.MichiganTexas.GameId);
            michiganTexas.MyOutcome.Should().Be(InfluenceOutcome.Won);
            michiganTexas.Game.WinnerTeamId.Should().Be(scenario.MichiganTexas.HomeTeam.TeamId);

            // Won on the decided game (10) plus still in play on the other pending pick (10).
            body.PointsSoFar.Should().Be(10);
            body.MaxRemaining.Should().Be(10);

            // Dance picked Texas on the same game, so it reads Lost for her instead.
            using HttpClient dance = _fixture.PinnedFactory.CreateClientAs(scenario.UserIdByName["Dance"]);
            using HttpResponseMessage danceResponse = await dance.GetAsync(scenario.DashboardRoute);
            DashboardResponse? danceBody = await danceResponse.Content.ReadFromJsonAsync<DashboardResponse>();
            danceBody!.Games.Single(g => g.Game.GameId == scenario.MichiganTexas.GameId).MyOutcome
                .Should().Be(InfluenceOutcome.Lost);
            danceBody.PointsSoFar.Should().Be(0);
        }
        finally
        {
            await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
            {
                Game game = await db.Games.SingleAsync(candidate => candidate.Id == scenario.MichiganTexas.GameId);
                game.Status = GameStatus.Scheduled;
                game.HomeScore = null;
                game.AwayScore = null;
                await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task GivenAMemberWhoJoinedAfterLock_WhenTheyAskForTheirOwnDashboard_ThenTheySeeNoPickEverywhere()
    {
        DashboardWeekScenario scenario = await DashboardWeekScenario.CreateAsync(_fixture.PinnedFactory);
        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        Guid latecomerUserId = await _fixture.PinnedFactory.QueryDbAsync(async db =>
        {
            League league = await db.Leagues.SingleAsync(l => l.Id == scenario.LeagueId);
            User user = await TestUsers.CreateUserAsync(db, "Latecomer");
            await TestUsers.CreateMembershipAsync(db, league, user);
            return user.Id;
        });

        using HttpClient latecomer = _fixture.PinnedFactory.CreateClientAs(latecomerUserId);
        using HttpResponseMessage response = await latecomer.GetAsync(scenario.DashboardRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        body!.IsAvailable.Should().BeTrue();
        body.Games.Should().HaveCount(2);
        body.Games.Should().OnlyContain(g => g.MyTeamId == null && g.MyOutcome == InfluenceOutcome.NoPick);
        body.PointsSoFar.Should().Be(0);
        body.MaxRemaining.Should().Be(0, "a member with no pick has nothing still in play");

        // They cannot be named in anyone's lists either - they were never active at lock.
        DashboardGameDto michiganTexas = body.Games.Single(g => g.Game.GameId == scenario.MichiganTexas.GameId);
        michiganTexas.NoPick.Should().NotContain(m => m.MembershipId == latecomerUserId);
        michiganTexas.OppositePicks.Should().NotContain(m => m.MembershipId == latecomerUserId);
    }

    [Fact]
    public async Task GivenAFormerMemberWhoPickedBeforeLeaving_WhenTheWeekLocks_ThenTheyStillAppearFlagged()
    {
        DashboardWeekScenario scenario = await DashboardWeekScenario.CreateAsync(_fixture.PinnedFactory);

        Guid danceMembershipId = scenario.MembershipIdByName["Dance"];
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            Membership dance = await db.Memberships.SingleAsync(m => m.Id == danceMembershipId);
            dance.RemovedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime;
            await db.SaveChangesAsync();
        });

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpClient michael = _fixture.PinnedFactory.CreateClientAs(scenario.UserIdByName["Michael"]);
        using HttpResponseMessage response = await michael.GetAsync(scenario.DashboardRoute);
        DashboardResponse? body = await response.Content.ReadFromJsonAsync<DashboardResponse>();

        DashboardGameDto michiganTexas = body!.Games.Single(g => g.Game.GameId == scenario.MichiganTexas.GameId);
        MemberRef danceRef = michiganTexas.OppositePicks.Single(m => m.MembershipId == danceMembershipId);
        danceRef.IsFormer.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAnExtraQueryParameter_WhenAMemberAsksForTheDashboard_ThenItIs400()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/dashboard?asMember=someone-else");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenTheAuthMatrix_WhenAskingForTheDashboard_ThenOnlyAMemberMaySeeIt()
    {
        await AuthMatrix.RunAsync(
            _fixture,
            HttpMethod.Get,
            $"/api/leagues/{AuthMatrix.LeagueIdPlaceholder}/weeks/1/dashboard");
    }
}
