using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>POST .../picks/me/ack-changes</c> and the recompute helpers P4-04's
/// <c>GameAddedToSet</c>/<c>GameRemovedFromSet</c> handlers will call
/// (<see cref="PickService.RecomputeStatusAsync"/>, <see cref="PickService.RecomputeWeekStatusesAsync"/>).
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AckChangesTests : IAsyncLifetime
{
    private readonly ClockedApp _app;

    public AckChangesTests(ApiTestFixture fixture)
    {
        _app = new ClockedApp(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task GivenAMemberWhoHasNeverTouchedTheWeek_WhenAcking_ThenItIs204AndNothingIsWritten()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();

        using HttpResponseMessage response = await client.PostAsync($"{scenario.PicksRoute}/me/ack-changes", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadSubmissionAsync(scenario)).Should().BeNull();
    }

    [Fact]
    public async Task GivenUnseenGameChanges_WhenAcking_ThenTheFlagClears()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAsync(client, scenario, scenario.Games[0]);

        await RecomputeWeekAsync(scenario, markUnseenFor: [scenario.MemberMembershipId]);

        using (HttpResponseMessage before = await client.GetAsync($"{scenario.PicksRoute}/me"))
        {
            MyPicksResponse? body = await before.Content.ReadFromJsonAsync<MyPicksResponse>();
            body!.HasUnseenGameChanges.Should().BeTrue();
        }

        using HttpResponseMessage ack = await client.PostAsync($"{scenario.PicksRoute}/me/ack-changes", null);
        ack.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using HttpResponseMessage after = await client.GetAsync($"{scenario.PicksRoute}/me");
        MyPicksResponse? afterBody = await after.Content.ReadFromJsonAsync<MyPicksResponse>();
        afterBody!.HasUnseenGameChanges.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAWeekOutsideTheLeaguesRange_WhenAcking_ThenItIs404()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/99/picks/me/ack-changes", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenASubmittedMemberAndANewGame_WhenTheWeekIsRecomputed_ThenTheyRevertAndAreFlagged()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        foreach (GameSetGameDto game in scenario.Games)
        {
            await PickAsync(client, scenario, game);
        }

        using (HttpResponseMessage submit = await client.PostAsync($"{scenario.PicksRoute}/me/submit", null))
        {
            submit.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _app.Advance(TimeSpan.FromMinutes(30));

        Guid extra = await FixtureGameData.GetGameIdAsync(_app.Factory, 700005);
        using HttpClient commish = _app.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using (HttpResponseMessage add = await commish.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/gameset/games",
            new AddGameRequest(extra)))
        {
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Exactly what P4-04's GameAddedToSet handler will do.
        int written = await RecomputeWeekAsync(scenario, markUnseenFor: [scenario.MemberMembershipId]);

        written.Should().BeGreaterThan(0);
        WeekSubmission? submission = await ReadSubmissionAsync(scenario);
        submission!.Status.Should().Be(SubmissionStatus.InProgress);
        submission.HasUnseenGameChanges.Should().BeTrue();
        submission.SubmittedUtc.Should().NotBeNull("the member did submit once; it is the added game that reverted them");
    }

    [Fact]
    public async Task GivenALockedWeek_WhenRecomputing_ThenTheLockJobsStatusSurvives()
    {
        (PickWeekScenario scenario, HttpClient client) = await CreateAsync();
        await PickAsync(client, scenario, scenario.Games[0]);

        await _app.Factory.ExecuteDbAsync(async db =>
        {
            WeekSubmission submission = await db.WeekSubmissions.SingleAsync(
                row => row.WeekGameSetId == scenario.WeekGameSetId
                    && row.MembershipId == scenario.MemberMembershipId);
            submission.Status = SubmissionStatus.Incomplete;
            await db.SaveChangesAsync();
        });

        await scenario.MarkLockedAsync(_app.Factory, _app.NowUtc.UtcDateTime);

        int written = await RecomputeWeekAsync(scenario, markUnseenFor: null);

        written.Should().Be(0);
        WeekSubmission? submission = await ReadSubmissionAsync(scenario);
        submission!.Status.Should().Be(SubmissionStatus.Incomplete);
    }

    [Fact]
    public async Task GivenAMemberWithNoRow_WhenRecomputingOneMemberWithTheFlag_ThenARowAppears()
    {
        (PickWeekScenario scenario, _) = await CreateAsync();

        await using AsyncServiceScope scope = _app.Factory.Services
            .GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        PickService picks = scope.ServiceProvider.GetRequiredService<PickService>();

        await picks.RecomputeStatusAsync(
            scenario.WeekGameSetId, scenario.SecondMemberMembershipId, markUnseenGameChanges: true, default);

        WeekSubmission? submission = await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.WeekGameSetId == scenario.WeekGameSetId
                && row.MembershipId == scenario.SecondMemberMembershipId));

        submission!.Status.Should().Be(SubmissionStatus.NotStarted);
        submission.HasUnseenGameChanges.Should().BeTrue();
    }

    private async Task<int> RecomputeWeekAsync(PickWeekScenario scenario, IReadOnlyCollection<Guid>? markUnseenFor)
    {
        await using AsyncServiceScope scope = _app.Factory.Services
            .GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        PickService picks = scope.ServiceProvider.GetRequiredService<PickService>();
        return await picks.RecomputeWeekStatusesAsync(scenario.WeekGameSetId, markUnseenFor, default);
    }

    private async Task<(PickWeekScenario Scenario, HttpClient Client)> CreateAsync()
    {
        _app.Set(ApiTestFixture.PinnedNowUtc);
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_app.Factory);
        return (scenario, _app.Factory.CreateMutatingClientAs(scenario.MemberUserId));
    }

    private async Task<WeekSubmission?> ReadSubmissionAsync(PickWeekScenario scenario) =>
        await _app.Factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.WeekGameSetId == scenario.WeekGameSetId
                && row.MembershipId == scenario.MemberMembershipId));

    private static async Task PickAsync(HttpClient client, PickWeekScenario scenario, GameSetGameDto game)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
