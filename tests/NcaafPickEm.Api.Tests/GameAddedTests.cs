using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="GameAddedPickHandler"/> (Feature 04, P4-04): a member's stored submission row
/// reacts to <c>GameAddedToSet</c> the instant the event is dispatched, not only the next time
/// they call a picks endpoint. Runs on its own <see cref="ClockedApp"/> for the same reason
/// <c>SubmitTests</c> does.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class GameAddedTests : IAsyncLifetime
{
    private readonly ClockedApp _app;

    public GameAddedTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GivenASubmittedMember_WhenAGameIsAdded_ThenTheStoredRowRevertsAndFlagsImmediately()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        _app.Advance(TimeSpan.FromHours(1));

        Guid extra = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005); // eligible, never in Top25
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(extra)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // The stored row, not a live recompute triggered by this request: nothing here calls
        // any picks endpoint for the member between the add and this read.
        WeekSubmission stored = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);
        stored.Status.Should().Be(SubmissionStatus.InProgress);
        stored.HasUnseenGameChanges.Should().BeTrue();

        // GET /api/leagues reads WeekSubmissions.Status directly (LeagueService), so it is the
        // sharpest check that the write actually happened rather than being derived on read.
        using HttpResponseMessage mine = await member.GetAsync("/api/leagues");
        LeagueSummary[]? leagues = await mine.Content.ReadFromJsonAsync<LeagueSummary[]>();
        leagues!.Single(l => l.LeagueId == scenario.LeagueId).MyCurrentWeekStatus.Should().Be(SubmissionStatus.InProgress);

        using HttpResponseMessage picksMe = await member.GetAsync($"{scenario.PicksRoute}/me");
        MyPicksResponse? picksBody = await picksMe.Content.ReadFromJsonAsync<MyPicksResponse>();
        picksBody!.Games.Should().ContainSingle(row => row.Game.GameId == extra && row.IsNewSinceSubmit);

        using HttpResponseMessage roster = await commish.GetAsync($"{scenario.PicksRoute}/status");
        MemberStatusRow[]? rosterBody = await roster.Content.ReadFromJsonAsync<MemberStatusRow[]>();
        rosterBody!.Single(row => row.MembershipId == scenario.MemberMembershipId).Status.Should().Be(SubmissionStatus.InProgress);
    }

    [Fact]
    public async Task GivenANotStartedMember_WhenAGameIsAdded_ThenNothingChangesForThem()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        // Second member never touches picks at all.

        Guid extra = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005);
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(extra)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        bool hasRow = await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .AnyAsync(s => s.WeekGameSetId == scenario.WeekGameSetId && s.MembershipId == scenario.SecondMemberMembershipId));
        hasRow.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAnInProgressMember_WhenAGameIsAdded_ThenTheyStayInProgressWithTheFlagSet()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAsync(member, scenario, scenario.Games[0]); // one pick, no submit -> InProgress

        Guid extra = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005);
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(extra)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        WeekSubmission stored = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);
        stored.Status.Should().Be(SubmissionStatus.InProgress);
        stored.HasUnseenGameChanges.Should().BeTrue();
    }

    [Fact]
    public async Task GivenTheFlagIsSet_WhenAckChangesIsCalled_ThenItClears()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        Guid extra = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005);
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(extra)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await ReadSubmissionAsync(scenario, scenario.MemberMembershipId)).HasUnseenGameChanges.Should().BeTrue();

        using HttpResponseMessage ack = await member.PostAsync($"{scenario.PicksRoute}/me/ack-changes", null);
        ack.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadSubmissionAsync(scenario, scenario.MemberMembershipId)).HasUnseenGameChanges.Should().BeFalse();
    }

    [Fact]
    public async Task GivenALockedWeek_WhenGameAddedToSetIsHandled_ThenNothingChanges()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        await scenario.MarkLockedAsync(_app.Factory, _app.NowUtc.UtcDateTime);

        // Read the row the lock left behind (Submitted -> Locked): what must not move afterwards is
        // the locked verdict, not the pre-lock status.
        WeekSubmission before = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);

        // No live path can raise GameAddedToSet against an already-locked week (every raise site
        // refuses before saving), so the handler's own defensive guard is exercised directly.
        await using AsyncServiceScope scope = _app.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
        GameAddedPickHandler handler = ActivatorUtilities.CreateInstance<GameAddedPickHandler>(scope.ServiceProvider);

        await handler.HandleAsync(
            new GameAddedToSet(scenario.LeagueId, PickWeekScenario.Week, scenario.WeekGameSetId, [Guid.CreateVersion7()], "Manual")
            {
                OccurredUtc = _app.NowUtc.UtcDateTime,
            },
            CancellationToken.None);

        WeekSubmission after = await ReadSubmissionAsync(scenario, scenario.MemberMembershipId);
        after.Status.Should().Be(before.Status);
        after.HasUnseenGameChanges.Should().Be(before.HasUnseenGameChanges);
        after.LastChangedUtc.Should().Be(before.LastChangedUtc);
    }

    [Fact]
    public async Task GivenARegenerationThatAddsTwoGames_WhenHandled_ThenEachMemberGetsExactlyOneRow()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        using HttpClient member = _app.Factory.CreateMutatingClientAs(scenario.MemberUserId);
        await PickAllAsync(member, scenario);
        await SubmitAsync(member, scenario);

        using HttpClient second = _app.Factory.CreateMutatingClientAs(scenario.SecondMemberUserId);
        await PickAsync(second, scenario, scenario.Games[0]);

        // Rank two more, currently-unranked teams so the Top25 rule picks up two more games
        // (700008 Oklahoma, 700009) in a single regeneration - one GameAddedToSet event carrying
        // both GameSetGameIds, not two separate events.
        Guid oklahomaTeamId = await FixtureGameData.GetTeamIdAsync(_app.Factory, 900111);
        Guid otherTeamId = await FixtureGameData.GetTeamIdAsync(_app.Factory, 900113);
        try
        {
            await _app.Factory.QueryDbAsync(async db =>
            {
                db.Rankings.AddRange(
                    new Ranking { SeasonYear = 2026, Week = 7, Poll = Ranking.ApPoll, Rank = 12, TeamId = oklahomaTeamId, FetchedUtc = DateTime.UtcNow },
                    new Ranking { SeasonYear = 2026, Week = 7, Poll = Ranking.ApPoll, Rank = 13, TeamId = otherTeamId, FetchedUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
                return true;
            });

            using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
            using HttpResponseMessage regenerate = await commish.PostAsync(
                $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/generate", null);
            regenerate.StatusCode.Should().Be(HttpStatusCode.OK);
            WeekGameSetResponse? regenerated = await regenerate.Content.ReadFromJsonAsync<WeekGameSetResponse>();
            regenerated!.Games.Length.Should().Be(scenario.Games.Length + 2);

            List<WeekSubmission> rows = await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
                .AsNoTracking()
                .Where(s => s.WeekGameSetId == scenario.WeekGameSetId)
                .ToListAsync());

            rows.Should().HaveCount(2);
            rows.Should().OnlyContain(row => row.HasUnseenGameChanges);
            rows.Count(row => row.MembershipId == scenario.MemberMembershipId).Should().Be(1);
            rows.Count(row => row.MembershipId == scenario.SecondMemberMembershipId).Should().Be(1);
            rows.Single(row => row.MembershipId == scenario.MemberMembershipId).Status.Should().Be(SubmissionStatus.InProgress);
            rows.Single(row => row.MembershipId == scenario.SecondMemberMembershipId).Status.Should().Be(SubmissionStatus.InProgress);
        }
        finally
        {
            await _app.Factory.ExecuteDbAsync(async db =>
            {
                await db.Rankings
                    .Where(r => r.SeasonYear == 2026 && r.Week == 7 && (r.TeamId == oklahomaTeamId || r.TeamId == otherTeamId))
                    .ExecuteDeleteAsync();
            });
        }
    }

    private async Task<WeekSubmission> ReadSubmissionAsync(PickWeekScenario scenario, Guid membershipId) =>
        await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .SingleAsync(submission => submission.WeekGameSetId == scenario.WeekGameSetId
                && submission.MembershipId == membershipId));

    private static async Task PickAllAsync(HttpClient client, PickWeekScenario scenario)
    {
        foreach (GameSetGameDto game in scenario.Games)
        {
            await PickAsync(client, scenario, game);
        }
    }

    private static async Task PickAsync(HttpClient client, PickWeekScenario scenario, GameSetGameDto game)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SubmitAsync(HttpClient client, PickWeekScenario scenario)
    {
        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
