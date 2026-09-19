using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="LockWeekJob"/> (Features 04 and 05, <c>04-Domain-Algorithms.md</c> section 5): what
/// is due, what one run writes, and that a second run writes nothing.
/// </summary>
/// <remarks>
/// Its own <see cref="ApiFactory"/> over the run's shared database, because the class needs both
/// a movable clock (the week has to reach its lock instant) and a <see cref="WeekLocked"/>
/// subscriber, which <see cref="ClockedApp"/> does not wire.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class LockWeekJobTests : IAsyncLifetime
{
    private readonly FixedTimeProvider _clock = new(ApiTestFixture.PinnedNowUtc);
    private readonly WeekLockedRecorder _recorder = new();
    private readonly ApiTestFixture _fixture;
    private readonly ApiFactory _factory;

    public LockWeekJobTests(ApiTestFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
        _factory = new ApiFactory(
            fixture.Database.ConnectionString,
            timeProvider: _clock,
            configureServices: services =>
            {
                services.AddSingleton(_recorder);
                services.AddDomainEventHandler<WeekLocked, RecordingWeekLockedHandler>();
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GivenAWeekWhoseLockInstantHasPassed_WhenAskingWhatIsDue_ThenItIsOfferedAtItsLockInstant()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        IReadOnlyList<OneShotOccurrence> due = await GetDueAsync(lockAtUtc);

        OneShotOccurrence mine = due.Should().ContainSingle(occurrence => occurrence.Key == Key(scenario)).Subject;
        mine.DueUtc.Should().Be(new DateTimeOffset(lockAtUtc, TimeSpan.Zero));
    }

    [Fact]
    public async Task GivenAWeekWhoseLockInstantIsStillAhead_WhenAskingWhatIsDue_ThenItIsNotOffered()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        IReadOnlyList<OneShotOccurrence> due = await GetDueAsync(lockAtUtc.AddSeconds(-1));

        due.Should().NotContain(occurrence => occurrence.Key == Key(scenario));
    }

    /// <remarks>
    /// The whole of the card's "swept every minute Friday 18:00 to Sunday 03:00 Eastern": there is
    /// no window, only a query, so a week whose lock instant went by while the process was down is
    /// still offered on the first tick after it comes back.
    /// </remarks>
    [Fact]
    public async Task GivenAWeekThatWentUnlockedForHours_WhenAskingWhatIsDue_ThenItIsStillOffered()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        IReadOnlyList<OneShotOccurrence> due = await GetDueAsync(lockAtUtc.AddHours(9));

        due.Should().Contain(occurrence => occurrence.Key == Key(scenario));
    }

    [Fact]
    public async Task GivenADueWeek_WhenTheJobRuns_ThenLockedUtcIsWhenItRanNotWhenItWasDue()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        DateTime ranAt = lockAtUtc.AddMinutes(3);
        _clock.Set(new DateTimeOffset(ranAt, TimeSpan.Zero));
        await RunJobAsync(scenario, lockAtUtc);

        DateTime? lockedUtc = await LoadLockedUtcAsync(scenario);
        lockedUtc.Should().Be(ranAt);
    }

    [Fact]
    public async Task GivenADueWeek_WhenTheJobRuns_ThenEachActiveGameHasItsSpreadAndPointValueFrozen()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        await LockAsync(scenario, lockAtUtc);

        List<WeekGameSetGame> rows = await LoadRowsAsync(scenario);
        rows.Should().HaveCount(scenario.Games.Length);
        rows.Should().OnlyContain(row => row.ResolvedPointValue > 0);

        // The fixture puts a consensus line on Michigan/Texas (700001), so at least one row must
        // come out of the lock with a spread rather than a null.
        Guid michiganTexas = await FixtureGameData.GetGameIdAsync(_factory, 700001);
        WeekGameSetGame ranked = rows.Single(row => row.GameId == michiganTexas);
        ranked.SpreadAtLock.Should().Be(-7.5m);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenAPointRuleAndTheLineChange_ThenTheFrozenValuesDoNotMove()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();
        await LockAsync(scenario, lockAtUtc);

        Guid michiganTexas = await FixtureGameData.GetGameIdAsync(_factory, 700001);
        WeekGameSetGame before = (await LoadRowsAsync(scenario)).Single(row => row.GameId == michiganTexas);
        before.SpreadAtLock.Should().Be(-7.5m);

        Guid lateLineId = Guid.CreateVersion7();
        try
        {
            // A line that arrives after the week locked, and a rule that would have doubled the
            // value of every game in it.
            await _factory.ExecuteDbAsync(async db =>
            {
                db.GameLines.Add(new GameLine
                {
                    Id = lateLineId,
                    GameId = michiganTexas,
                    Provider = "test-late",
                    Spread = -1m,
                    FetchedUtc = lockAtUtc.AddHours(1),
                });
                await db.SaveChangesAsync();
            });

            using HttpClient commish = _factory.CreateMutatingClientAs(scenario.CommissionerUserId);
            PointRuleDto[] rules =
            [
                new(null, 0, PointRuleType.CloseSpread, null, null, SpreadThreshold: 30m, PointValue: 99),
            ];

            using HttpResponseMessage response = await commish.PutAsJsonAsync(
                $"/api/leagues/{scenario.LeagueId}/point-rules", rules);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            WeekGameSetGame after = (await LoadRowsAsync(scenario)).Single(row => row.GameId == michiganTexas);
            after.SpreadAtLock.Should().Be(before.SpreadAtLock);
            after.ResolvedPointValue.Should().Be(before.ResolvedPointValue);
            after.ResolvedPointValue.Should().NotBe(99);
        }
        finally
        {
            await _factory.ExecuteDbAsync(async db =>
            {
                await db.GameLines.Where(line => line.Id == lateLineId).ExecuteDeleteAsync();
            });
        }
    }

    [Fact]
    public async Task GivenASubmitterAndAPartialPicker_WhenTheJobRuns_ThenOneIsLockedAndTheOtherIncomplete()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        using (HttpClient submitter = _factory.CreateMutatingClientAs(scenario.MemberUserId))
        {
            await PickEveryGameAsync(submitter, scenario);
            using HttpResponseMessage submit = await submitter.PostAsync($"{scenario.PicksRoute}/me/submit", null);
            submit.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (HttpClient partial = _factory.CreateMutatingClientAs(scenario.SecondMemberUserId))
        {
            GameSetGameDto first = scenario.Games[0];
            using HttpResponseMessage pick = await partial.PutAsJsonAsync(
                $"{scenario.PicksRoute}/me/{first.GameId}", new SetPickRequest(first.HomeTeam.TeamId));
            pick.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await LockAsync(scenario, lockAtUtc);

        Dictionary<Guid, SubmissionStatus> statuses = await LoadStatusesAsync(scenario);

        statuses[scenario.MemberMembershipId].Should().Be(SubmissionStatus.Locked);
        statuses[scenario.SecondMemberMembershipId].Should().Be(SubmissionStatus.Incomplete);
    }

    [Fact]
    public async Task GivenACommissionerWhoNeverPicked_WhenTheJobRuns_ThenTheyStillGetAnIncompleteRow()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        await LockAsync(scenario, lockAtUtc);

        Guid commissionerMembershipId = await _factory.QueryDbAsync(db => db.Memberships
            .Where(membership => membership.LeagueId == scenario.LeagueId
                && membership.UserId == scenario.CommissionerUserId)
            .Select(membership => membership.Id)
            .SingleAsync());

        Dictionary<Guid, SubmissionStatus> statuses = await LoadStatusesAsync(scenario);
        statuses[commissionerMembershipId].Should().Be(SubmissionStatus.Incomplete);
    }

    [Fact]
    public async Task GivenAMemberWhoJoinedAfterTheLockInstant_WhenTheJobRuns_ThenTheyGetNoRow()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        Guid lateMembershipId = await AddLateJoinerAsync(scenario, lockAtUtc.AddMinutes(5));

        await LockAsync(scenario, lockAtUtc);

        Dictionary<Guid, SubmissionStatus> statuses = await LoadStatusesAsync(scenario);
        statuses.Should().NotContainKey(lateMembershipId);
        statuses.Should().ContainKey(scenario.MemberMembershipId);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenTheJobRunsAgain_ThenNothingChangesAndNoSecondEventIsRaised()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        await LockAsync(scenario, lockAtUtc);

        DateTime? firstLockedUtc = await LoadLockedUtcAsync(scenario);
        Dictionary<Guid, SubmissionStatus> firstStatuses = await LoadStatusesAsync(scenario);
        List<WeekGameSetGame> firstRows = await LoadRowsAsync(scenario);

        // A whole hour later, and the same occurrence offered again: the second run must find
        // LockedUtc set and return without touching anything (section 5's idempotency clause).
        _clock.Set(new DateTimeOffset(lockAtUtc.AddHours(1), TimeSpan.Zero));
        await RunJobAsync(scenario, lockAtUtc);

        (await LoadLockedUtcAsync(scenario)).Should().Be(firstLockedUtc);
        (await LoadStatusesAsync(scenario)).Should().BeEquivalentTo(firstStatuses);
        (await LoadRowsAsync(scenario))
            .Select(row => (row.Id, row.SpreadAtLock, row.ResolvedPointValue))
            .Should().Equal(firstRows.Select(row => (row.Id, row.SpreadAtLock, row.ResolvedPointValue)));

        _recorder.For(scenario.WeekGameSetId).Should().HaveCount(1);
    }

    [Fact]
    public async Task GivenADueWeek_WhenTheJobRuns_ThenWeekLockedIsRaisedOnceWithTheWeekAndLeague()
    {
        (PickWeekScenario scenario, DateTime lockAtUtc) = await CreateAsync();

        await LockAsync(scenario, lockAtUtc);

        WeekLocked raised = _recorder.For(scenario.WeekGameSetId).Should().ContainSingle().Subject;
        raised.LeagueId.Should().Be(scenario.LeagueId);
        raised.Week.Should().Be(PickWeekScenario.Week);
        raised.LockedUtc.Should().Be(await LoadLockedUtcAsync(scenario));
        raised.OccurredUtc.Should().Be(raised.LockedUtc);
    }

    /// <remarks>
    /// Two halves of one claim. First, the app's own container really registers the job (the
    /// <c>AddOneShotJob&lt;LockWeekJob&gt;()</c> line in
    /// <c>Infrastructure/DependencyInjection.cs</c>). Second, a <see cref="SchedulerTick"/> over
    /// the same database locks the week and records exactly one <c>JobRuns</c> row for it, and a
    /// second tick adds none because the week no longer answers <c>GetDueAsync</c>. The tick runs
    /// on a <see cref="SchedulerHarness"/> rather than the app's own scheduler so the pass carries
    /// only this job — ticking the whole registered set would fire the cron jobs too and fill the
    /// shared database's <c>JobRuns</c> table with heartbeats dated in the fixture season.
    /// </remarks>
    [Fact]
    public async Task GivenTheRegisteredScheduler_WhenTicked_ThenTheWeekLocksAndIsRecordedOnce()
    {
        await using (AsyncServiceScope scope = Scope())
        {
            scope.ServiceProvider.GetServices<IOneShotJob>()
                .Should().ContainSingle(job => job is LockWeekJob);
        }

        // The fixture's earliest Saturday-Eastern kickoff (USC/Stanford, 00:30 ET) as the week's
        // only game, so this tick's instant is before every other league's lock instant in the
        // shared database and the pass has exactly one week to do.
        (Guid weekGameSetId, DateTime lockAtUtc) = await CreateEarliestKickoffWeekAsync();

        var tickAt = new DateTimeOffset(lockAtUtc.AddMinutes(1), TimeSpan.Zero);
        _clock.Set(tickAt);

        await using SchedulerHarness harness = SchedulerHarness.Create(_fixture, services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddDomainEvents();
            services.AddOneShotJob<LockWeekJob>();
        });

        await harness.Tick.TickAsync(tickAt, CancellationToken.None);
        await harness.Tick.TickAsync(tickAt.AddMinutes(1), CancellationToken.None);

        DateTime? lockedUtc = await _factory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .Where(set => set.Id == weekGameSetId)
            .Select(set => set.LockedUtc)
            .SingleAsync());
        lockedUtc.Should().Be(tickAt.UtcDateTime);

        string runName = $"LockWeek:{weekGameSetId:N}";
        List<JobRun> runs = await _factory.QueryDbAsync(db => db.JobRuns
            .AsNoTracking()
            .Where(run => run.JobName == runName)
            .ToListAsync());

        runs.Should().ContainSingle().Which.ScheduledForUtc.Should().Be(lockAtUtc);
        runs[0].Success.Should().BeTrue();
    }

    private static string Key(PickWeekScenario scenario) => scenario.WeekGameSetId.ToString("N");

    private AsyncServiceScope Scope() =>
        _factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

    private async Task<(PickWeekScenario Scenario, DateTime LockAtUtc)> CreateAsync()
    {
        _clock.Set(ApiTestFixture.PinnedNowUtc);
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_factory);

        DateTime lockAtUtc = await _factory.QueryDbAsync(db => db.WeekGameSets
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.LockAtUtc!.Value)
            .SingleAsync());

        return (scenario, lockAtUtc);
    }

    /// <summary>
    /// A league of its own whose week 7 holds only fixture game 700016 (USC/Stanford, the
    /// Friday-Pacific kickoff that is Saturday 00:30 Eastern), so its lock instant is hours
    /// earlier than any other league's in the shared database.
    /// </summary>
    private async Task<(Guid WeekGameSetId, DateTime LockAtUtc)> CreateEarliestKickoffWeekAsync()
    {
        _clock.Set(ApiTestFixture.PinnedNowUtc);
        await FixtureGameData.EnsureSeededAsync(_factory);

        User commissioner = await _factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "Early Commish"));
        League league = await _factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _factory.QueryDbAsync(db =>
            TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));

        Guid earliestGameId = await FixtureGameData.GetGameIdAsync(_factory, 700016);

        using (HttpClient commish = _factory.CreateMutatingClientAs(commissioner.Id))
        {
            using HttpResponseMessage add = await commish.PostAsJsonAsync(
                $"/api/leagues/{league.Id}/weeks/{PickWeekScenario.Week}/gameset/games",
                new AddGameRequest(earliestGameId));
            add.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return await _factory.QueryDbAsync(async db =>
        {
            var row = await db.WeekGameSets
                .AsNoTracking()
                .Where(set => set.LeagueId == league.Id && set.Week == PickWeekScenario.Week)
                .Select(set => new { set.Id, set.LockAtUtc })
                .SingleAsync();

            return (row.Id, row.LockAtUtc!.Value);
        });
    }

    private async Task LockAsync(PickWeekScenario scenario, DateTime lockAtUtc)
    {
        _clock.Set(new DateTimeOffset(lockAtUtc, TimeSpan.Zero));
        await RunJobAsync(scenario, lockAtUtc);
    }

    private async Task RunJobAsync(PickWeekScenario scenario, DateTime dueUtc)
    {
        await using AsyncServiceScope scope = Scope();
        LockWeekJob job = ActivatorUtilities.CreateInstance<LockWeekJob>(scope.ServiceProvider);
        await job.RunAsync(
            new OneShotOccurrence(Key(scenario), new DateTimeOffset(dueUtc, TimeSpan.Zero)),
            CancellationToken.None);
    }

    private async Task<IReadOnlyList<OneShotOccurrence>> GetDueAsync(DateTime nowUtc)
    {
        await using AsyncServiceScope scope = Scope();
        LockWeekJob job = ActivatorUtilities.CreateInstance<LockWeekJob>(scope.ServiceProvider);
        return await job.GetDueAsync(new DateTimeOffset(nowUtc, TimeSpan.Zero), CancellationToken.None);
    }

    private async Task PickEveryGameAsync(HttpClient client, PickWeekScenario scenario)
    {
        foreach (GameSetGameDto game in scenario.Games)
        {
            using HttpResponseMessage pick = await client.PutAsJsonAsync(
                $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(game.HomeTeam.TeamId));
            pick.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private async Task<Guid> AddLateJoinerAsync(PickWeekScenario scenario, DateTime joinedUtc)
    {
        User user = await _factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Late {Guid.CreateVersion7()}"[..20]));

        return await _factory.QueryDbAsync(async db =>
        {
            var membership = new Membership
            {
                Id = Guid.CreateVersion7(),
                LeagueId = scenario.LeagueId,
                UserId = user.Id,
                Role = MembershipRole.Member,
                JoinedUtc = joinedUtc,
                JoinedWeek = PickWeekScenario.Week,
            };

            db.Memberships.Add(membership);
            await db.SaveChangesAsync();
            return membership.Id;
        });
    }

    private async Task<DateTime?> LoadLockedUtcAsync(PickWeekScenario scenario) =>
        await _factory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.LockedUtc)
            .SingleAsync());

    private async Task<List<WeekGameSetGame>> LoadRowsAsync(PickWeekScenario scenario) =>
        await _factory.QueryDbAsync(db => db.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId && !row.IsRemoved)
            .OrderBy(row => row.Id)
            .ToListAsync());

    private async Task<Dictionary<Guid, SubmissionStatus>> LoadStatusesAsync(PickWeekScenario scenario) =>
        await _factory.QueryDbAsync(db => db.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == scenario.WeekGameSetId)
            .ToDictionaryAsync(submission => submission.MembershipId, submission => submission.Status));

    /// <summary>Collects every <see cref="WeekLocked"/> the app dispatches during this class.</summary>
    private sealed class WeekLockedRecorder
    {
        private readonly List<WeekLocked> _events = [];

        public void Add(WeekLocked domainEvent)
        {
            lock (_events)
            {
                _events.Add(domainEvent);
            }
        }

        public IReadOnlyList<WeekLocked> For(Guid weekGameSetId)
        {
            lock (_events)
            {
                return [.. _events.Where(raised => raised.WeekGameSetId == weekGameSetId)];
            }
        }
    }

    private sealed class RecordingWeekLockedHandler : IDomainEventHandler<WeekLocked>
    {
        private readonly WeekLockedRecorder _recorder;

        public RecordingWeekLockedHandler(WeekLockedRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(WeekLocked domainEvent, CancellationToken cancellationToken)
        {
            _recorder.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
