using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Scoring.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Scoring;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Admin;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Feature 06 end to end on the fixture week: advance <see cref="FixtureSnapshotState"/> from 1 to
/// 6, poll after each, and watch <c>WeekResults</c> follow the games - including the Iowa State /
/// Kansas tie that earns nobody anything and the San José State / Hawai'i finish that lands after
/// midnight Eastern and still counts for week 7.
/// </summary>
/// <remarks>
/// Its own throwaway database and app, like <see cref="SaturdayPollerIntegrationTests"/>: this
/// class writes real scores onto the shared fixture <c>Games</c> rows, which must not leak into
/// the run's shared database. One test method, because the assertions are a sequence - each
/// snapshot's totals only mean something against the previous one's.
/// </remarks>
public sealed class ScoringSnapshotWalkTests
{
    [Fact]
    public async Task GivenALockedFixtureWeek_WhenWalkingEverySnapshot_ThenResultsFollowTheGamesAndTheTieHoldsTheWeekOpen()
    {
        var snapshotWriter = new RecordingStandingsSnapshotWriter();

        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(
            testDatabase.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IStandingsSnapshotWriter>();
                services.AddSingleton<IStandingsSnapshotWriter>(snapshotWriter);
            });

        ScoredWeekScenario scenario = await ScoredWeekScenario.CreateAsync(factory);
        FixtureSnapshotState snapshotState = factory.Services.GetRequiredService<FixtureSnapshotState>();

        // --- Snapshot 1: only the Friday-Pacific USC / Stanford game has finished ---------------

        await PollAsync(factory, snapshotState, 1);

        (await ResultsAsync(factory, scenario)).Should().HaveCount(
            3, "only the three memberships the lock job settled earn a result row");

        await AssertScoreAsync(factory, scenario, scenario.HomePickerMembershipId, points: 10, correct: 1);
        await AssertScoreAsync(factory, scenario, scenario.AwayPickerMembershipId, points: 0, correct: 0);
        await AssertScoreAsync(factory, scenario, scenario.PartialMembershipId, points: 0, correct: 0);
        (await IsCompleteAsync(factory, scenario)).Should().BeFalse();

        // A membership with no WeekSubmissions row was never active at lock (D-111) and must not
        // acquire a result just because the week is being scored.
        (await ResultsAsync(factory, scenario))
            .Should().NotContain(row => row.MembershipId == scenario.LateJoinerMembershipId);

        // --- Snapshot 3: five of the set's games are final, every one a home win ----------------

        await PollAsync(factory, snapshotState, 2);
        await PollAsync(factory, snapshotState, 3);

        await AssertScoreAsync(factory, scenario, scenario.HomePickerMembershipId, points: 50, correct: 5);
        await AssertScoreAsync(factory, scenario, scenario.AwayPickerMembershipId, points: 0, correct: 0);

        // --- Snapshot 5: everything but the late game is final, and one of them is a tie --------

        await PollAsync(factory, snapshotState, 4);
        await PollAsync(factory, snapshotState, 5);

        await AssertScoreAsync(factory, scenario, scenario.HomePickerMembershipId, points: 90, correct: 9);
        await AssertScoreAsync(factory, scenario, scenario.AwayPickerMembershipId, points: 20, correct: 2);
        await AssertScoreAsync(factory, scenario, scenario.PartialMembershipId, points: 10, correct: 1);

        // Iowa State 24 - Kansas 24 is Final with no winner: nobody is awarded its 15 points, and
        // it turns up on the data page's needs-review list exactly like a missing score does.
        await AssertTieNeedsReviewAsync(factory, scenario);

        // --- Snapshot 6: the 22:30 ET game goes final at 01:45 ET the next morning ---------------

        await PollAsync(factory, snapshotState, 6);

        await AssertScoreAsync(
            factory,
            scenario,
            scenario.HomePickerMembershipId,
            points: 90 + ScoredWeekScenario.LateGamePointValue,
            correct: 10);
        await AssertScoreAsync(factory, scenario, scenario.AwayPickerMembershipId, points: 20, correct: 2);

        (await IsCompleteAsync(factory, scenario)).Should().BeFalse(
            "the unresolved tie keeps the week open even though every game has finished");
        snapshotWriter.Calls.Should().BeEmpty("no week has completed yet");

        // --- The commissioner settles the tie ---------------------------------------------------

        await OverrideTieAsync(factory, scenario);

        await AssertScoreAsync(
            factory,
            scenario,
            scenario.HomePickerMembershipId,
            points: 90 + ScoredWeekScenario.LateGamePointValue,
            correct: 10);
        await AssertScoreAsync(
            factory,
            scenario,
            scenario.AwayPickerMembershipId,
            points: 20 + ScoredWeekScenario.TiePointValue,
            correct: 3);

        (await IsCompleteAsync(factory, scenario)).Should().BeTrue();
        snapshotWriter.CountFor(scenario.LeagueId, ScoredWeekScenario.Week).Should().Be(1);

        // --- Re-processing a final result is idempotent ------------------------------------------

        List<WeekResultSnapshot> before = await ResultsAsync(factory, scenario);

        await RescoreAsync(factory, scenario);
        await RescoreAsync(factory, scenario);

        (await ResultsAsync(factory, scenario)).Should().BeEquivalentTo(before);
        snapshotWriter.CountFor(scenario.LeagueId, ScoredWeekScenario.Week).Should().Be(
            1, "a week that was already complete does not write a second snapshot");
    }

    private static async Task PollAsync(ApiFactory factory, FixtureSnapshotState snapshotState, int snapshot)
    {
        snapshotState.Set(snapshot);

        await using AsyncServiceScope scope = factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await SaturdayPoller.PollOnceAsync(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            ScoredWeekScenario.FixtureSaturday,
            scope.ServiceProvider.GetRequiredService<ILogger<SaturdayPoller>>(),
            CancellationToken.None);
    }

    /// <summary>
    /// Writes the override the way P5-02's endpoint will - the row first, then the event - so the
    /// real <c>ResultOverriddenScoringHandler</c> is what rescores the week.
    /// </summary>
    private static async Task OverrideTieAsync(ApiFactory factory, ScoredWeekScenario scenario)
    {
        Guid gameId = await factory.QueryDbAsync(async database =>
        {
            WeekGameSetGame row = await database.WeekGameSetGames
                .SingleAsync(candidate => candidate.Id == scenario.TieGameSetGameId);

            row.ResultOverrideWinnerTeamId = scenario.TieAwayTeamId;
            await database.SaveChangesAsync();
            return row.GameId;
        });

        await using AsyncServiceScope scope = factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchAsync(
            [
                new ResultOverridden(
                    scenario.LeagueId,
                    ScoredWeekScenario.Week,
                    scenario.WeekGameSetId,
                    scenario.TieGameSetGameId,
                    gameId,
                    scenario.TieAwayTeamId,
                    scenario.HomePickerMembershipId)
                {
                    OccurredUtc = DateTime.UtcNow,
                },
            ],
            CancellationToken.None);
    }

    private static async Task RescoreAsync(ApiFactory factory, ScoredWeekScenario scenario)
    {
        await using AsyncServiceScope scope = factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<ScoringService>()
            .RescoreWeekAsync(scenario.WeekGameSetId, CancellationToken.None);
    }

    private static async Task AssertTieNeedsReviewAsync(ApiFactory factory, ScoredWeekScenario scenario)
    {
        using HttpClient commissioner = factory.CreateClientAs(scenario.CommissionerUserId);
        DataStatusResponse? status = await commissioner.GetFromJsonAsync<DataStatusResponse>(
            "/api/admin/data-status");

        status!.NeedsReview.Should().ContainSingle(row =>
            row.LeagueId == scenario.LeagueId && row.Reason == "Tie");
    }

    private static Task<bool> IsCompleteAsync(ApiFactory factory, ScoredWeekScenario scenario) =>
        factory.QueryDbAsync(database => database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.IsComplete)
            .SingleAsync());

    private static Task<List<WeekResultSnapshot>> ResultsAsync(ApiFactory factory, ScoredWeekScenario scenario) =>
        factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId)
            .OrderBy(row => row.MembershipId)
            .Select(row => new WeekResultSnapshot(
                row.MembershipId,
                row.Points,
                row.CorrectCount,
                row.ActiveGameCount,
                row.IsWeekComplete))
            .ToListAsync());

    private static async Task AssertScoreAsync(
        ApiFactory factory,
        ScoredWeekScenario scenario,
        Guid membershipId,
        int points,
        int correct)
    {
        WeekResult row = await factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .SingleAsync(candidate => candidate.WeekGameSetId == scenario.WeekGameSetId
                && candidate.MembershipId == membershipId));

        row.Points.Should().Be(points);
        row.CorrectCount.Should().Be(correct);
        row.ActiveGameCount.Should().Be(ScoredWeekScenario.GameCount);
    }

    /// <summary>The scoring columns of one <c>WeekResults</c> row, without the compute timestamp.</summary>
    private sealed record WeekResultSnapshot(
        Guid MembershipId,
        int Points,
        int CorrectCount,
        int ActiveGameCount,
        bool IsWeekComplete);
}
