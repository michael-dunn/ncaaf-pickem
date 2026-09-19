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

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="ScoringService"/> and <see cref="NightlyRescoreJob"/> against a week whose games
/// have all finished: what a void does, what an unlocked week does not do, and what the nightly
/// sweep picks up.
/// </summary>
/// <remarks>
/// Its own throwaway database, like <see cref="ScoringSnapshotWalkTests"/>, because
/// <see cref="InitializeAsync"/> writes the fixture's final scores onto the shared <c>Games</c>
/// rows once and every test then seeds its own league over them.
/// </remarks>
public sealed class ScoringServiceTests : IAsyncLifetime
{
    private readonly RecordingStandingsSnapshotWriter _snapshotWriter = new();
    private SqlTestDatabase _database = null!;
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _factory = new ApiFactory(
            _database.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IStandingsSnapshotWriter>();
                services.AddSingleton<IStandingsSnapshotWriter>(_snapshotWriter);
            });

        await FixtureGameData.EnsureSeededAsync(_factory);

        // Snapshot 6 is the end of the fixture timeline: every game final, including the tie and
        // the post-midnight finish. Applied once here so each test starts from a finished
        // Saturday and can concentrate on what scoring does with it.
        _factory.Services.GetRequiredService<FixtureSnapshotState>().Set(FixtureSnapshotState.MaxSnapshot);

        await using AsyncServiceScope scope = _factory.Services
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

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenAWeekThatHasNotLocked_WhenRescoring_ThenNoResultsAreWritten()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await _factory.ExecuteDbAsync(async database =>
        {
            WeekGameSet set = await database.WeekGameSets.SingleAsync(s => s.Id == scenario.WeekGameSetId);
            set.LockedUtc = null;
            await database.SaveChangesAsync();
        });

        WeekScoreResult? result = await RescoreAsync(scenario);

        result.Should().BeNull();
        (await ResultCountAsync(scenario)).Should().Be(
            0, "before lock the point values are not frozen and there is nothing to score");
    }

    [Fact]
    public async Task GivenAllGamesFinal_WhenRescoring_ThenTheUnresolvedTieKeepsTheWeekOpen()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        WeekScoreResult? result = await RescoreAsync(scenario);

        result!.IsWeekComplete.Should().BeFalse();
        result.NeedsReviewGameSetGameIds.Should().Equal(scenario.TieGameSetGameId);
        _snapshotWriter.CountFor(scenario.LeagueId, ScoredWeekScenario.Week).Should().Be(0);
    }

    /// <remarks>
    /// Feature 06: a voided game "is removed from scoring for that week and awards 0 to everyone",
    /// which also means it stops counting towards the week's total - "4 of 13" becomes "4 of 12".
    /// </remarks>
    [Fact]
    public async Task GivenAGameIsVoided_WhenRescoring_ThenItAwardsNothingAndLeavesActiveGameCount()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await RescoreAsync(scenario);

        int pointsBefore = await PointsAsync(scenario, scenario.HomePickerMembershipId);

        // The fixture's late game is a home win worth 35, so voiding it is visible in both numbers.
        await VoidAsync(scenario, scenario.LateGameSetGameId);

        int pointsAfter = await PointsAsync(scenario, scenario.HomePickerMembershipId);
        pointsAfter.Should().Be(pointsBefore - ScoredWeekScenario.LateGamePointValue);

        (await ActiveGameCountAsync(scenario, scenario.HomePickerMembershipId))
            .Should().Be(ScoredWeekScenario.GameCount - 1);
    }

    [Fact]
    public async Task GivenTheTieIsVoided_WhenRescoring_ThenTheWeekCompletesAndOneSnapshotIsWritten()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await RescoreAsync(scenario);

        await VoidAsync(scenario, scenario.TieGameSetGameId);

        (await IsCompleteAsync(scenario)).Should().BeTrue();
        _snapshotWriter.CountFor(scenario.LeagueId, ScoredWeekScenario.Week).Should().Be(1);

        // The week has already closed; recomputing it must not write a second snapshot and so
        // must not move anybody's trend arrow.
        await RescoreAsync(scenario);
        _snapshotWriter.CountFor(scenario.LeagueId, ScoredWeekScenario.Week).Should().Be(1);
    }

    [Fact]
    public async Task GivenAMembershipWithNoSubmissionRow_WhenRescoring_ThenItGetsNoResult()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        await RescoreAsync(scenario);

        List<Guid> scored = await _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId)
            .Select(row => row.MembershipId)
            .ToListAsync());

        scored.Should().HaveCount(3).And.NotContain(scenario.LateJoinerMembershipId);
    }

    /// <remarks>
    /// The orphan path: a stale <c>WeekResults</c> row whose membership the scorer no longer
    /// produces is deleted rather than left on the leaderboard, because a full recompute is
    /// authoritative about who has a result.
    /// </remarks>
    [Fact]
    public async Task GivenAStaleResultRow_WhenRescoring_ThenItIsRemoved()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await RescoreAsync(scenario);

        await _factory.ExecuteDbAsync(async database =>
        {
            database.WeekResults.Add(new WeekResult
            {
                WeekGameSetId = scenario.WeekGameSetId,
                MembershipId = scenario.LateJoinerMembershipId,
                Points = 999,
                CorrectCount = 9,
                ActiveGameCount = 9,
                IsWeekComplete = false,
                ComputedUtc = DateTime.UtcNow,
            });
            await database.SaveChangesAsync();
        });

        await RescoreAsync(scenario);

        (await ResultCountAsync(scenario)).Should().Be(3);
    }

    // --- The nightly sweep ---------------------------------------------------------------------

    [Fact]
    public void GivenTheNightlyJob_WhenReadingItsSchedule_ThenItRunsAt0430Eastern()
    {
        NightlyRescoreJob job = CreateJob(_factory.Services);

        job.Name.Should().Be("NightlyRescore");
        job.CronExpression.Should().Be("30 4 * * *");
    }

    [Fact]
    public async Task GivenALockedWeekNobodyScored_WhenTheNightlyJobRuns_ThenItIsScored()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        (await ResultCountAsync(scenario)).Should().Be(0, "nothing has scored this week yet");

        await RunNightlyJobAsync();

        (await ResultCountAsync(scenario)).Should().Be(3);
        (await PointsAsync(scenario, scenario.HomePickerMembershipId)).Should().BePositive();
    }

    /// <remarks>
    /// The second kind of suspect week: the set says complete while one of its own result rows
    /// says otherwise, which is what a rescore interrupted between the two writes leaves behind.
    /// </remarks>
    [Fact]
    public async Task GivenASetWhoseResultsDisagreeWithIt_WhenTheNightlyJobRuns_ThenTheyAreBroughtBackIntoLine()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await VoidAsync(scenario, scenario.TieGameSetGameId);
        (await IsCompleteAsync(scenario)).Should().BeTrue();

        await _factory.ExecuteDbAsync(async database =>
        {
            List<WeekResult> rows = await database.WeekResults
                .Where(row => row.WeekGameSetId == scenario.WeekGameSetId)
                .ToListAsync();

            foreach (WeekResult row in rows)
            {
                row.IsWeekComplete = false;
                row.Points = 0;
            }

            await database.SaveChangesAsync();
        });

        await RunNightlyJobAsync();

        List<WeekResult> repaired = await _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId)
            .ToListAsync());

        repaired.Should().OnlyContain(row => row.IsWeekComplete);
        repaired.Single(row => row.MembershipId == scenario.HomePickerMembershipId)
            .Points.Should().BePositive();
    }

    [Fact]
    public async Task GivenAnUnlockedWeek_WhenTheNightlyJobRuns_ThenItIsLeftAlone()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await _factory.ExecuteDbAsync(async database =>
        {
            WeekGameSet set = await database.WeekGameSets.SingleAsync(s => s.Id == scenario.WeekGameSetId);
            set.LockedUtc = null;
            await database.SaveChangesAsync();
        });

        await RunNightlyJobAsync();

        (await ResultCountAsync(scenario)).Should().Be(0);
    }

    private Task<ScoredWeekScenario> CreateScenarioAsync() => ScoredWeekScenario.CreateAsync(_factory);

    private static NightlyRescoreJob CreateJob(IServiceProvider services) =>
        ActivatorUtilities.CreateInstance<NightlyRescoreJob>(services);

    private async Task RunNightlyJobAsync()
    {
        await using AsyncServiceScope scope = _factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await CreateJob(scope.ServiceProvider).RunAsync(DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task<WeekScoreResult?> RescoreAsync(ScoredWeekScenario scenario)
    {
        await using AsyncServiceScope scope = _factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ScoringService>()
            .RescoreWeekAsync(scenario.WeekGameSetId, CancellationToken.None);
    }

    /// <summary>Voids one row the way P5-02's endpoint will: the row first, then the event.</summary>
    private async Task VoidAsync(ScoredWeekScenario scenario, Guid gameSetGameId)
    {
        Guid gameId = await _factory.QueryDbAsync(async database =>
        {
            WeekGameSetGame row = await database.WeekGameSetGames
                .SingleAsync(candidate => candidate.Id == gameSetGameId);

            row.IsVoided = true;
            await database.SaveChangesAsync();
            return row.GameId;
        });

        await using AsyncServiceScope scope = _factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchAsync(
            [
                new GameVoided(
                    scenario.LeagueId,
                    ScoredWeekScenario.Week,
                    scenario.WeekGameSetId,
                    gameSetGameId,
                    gameId,
                    "Weather",
                    scenario.HomePickerMembershipId)
                {
                    OccurredUtc = DateTime.UtcNow,
                },
            ],
            CancellationToken.None);
    }

    private Task<int> ResultCountAsync(ScoredWeekScenario scenario) =>
        _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .CountAsync(row => row.WeekGameSetId == scenario.WeekGameSetId));

    private Task<bool> IsCompleteAsync(ScoredWeekScenario scenario) =>
        _factory.QueryDbAsync(database => database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.IsComplete)
            .SingleAsync());

    private Task<int> PointsAsync(ScoredWeekScenario scenario, Guid membershipId) =>
        _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId && row.MembershipId == membershipId)
            .Select(row => row.Points)
            .SingleAsync());

    private Task<int> ActiveGameCountAsync(ScoredWeekScenario scenario, Guid membershipId) =>
        _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId && row.MembershipId == membershipId)
            .Select(row => row.ActiveGameCount)
            .SingleAsync());
}
