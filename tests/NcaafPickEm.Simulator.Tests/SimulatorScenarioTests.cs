using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Engine;
using NcaafPickEm.Simulator.Engine.Views;
using NcaafPickEm.Simulator.Tests.Infrastructure;

namespace NcaafPickEm.Simulator.Tests;

/// <summary>
/// The simulator's engine end to end against a real database, on a Tuesday: a sandbox, picks for
/// everyone, the lock, autoplay to a scored week, a season played out, rewinds and resets, and the
/// refusal to touch a database that is not its own.
/// </summary>
public sealed class SimulatorScenarioTests
{
    [Fact]
    public async Task GivenAFreshDatabase_WhenASandboxIsCreatedOnATuesday_ThenWeekOneIsCurrentAndLocksInFifteenMinutes()
    {
        await using SimHarness sim = await SimHarness.StartAsync();

        sim.Status.IsUsable.Should().BeTrue(sim.Status.Problem);
        SimSnapshot snapshot = await sim.CreateSandboxAsync();

        snapshot.Calendar.Current.Should().Be(new Domain.Seasons.CurrentWeek(1, Domain.Seasons.SeasonState.InSeason));
        snapshot.Games.Should().HaveCount(14);

        SimLeagueView league = snapshot.Leagues.Should().ContainSingle().Subject;
        league.Members.Should().HaveCount(6);
        league.Members.Should().ContainSingle(member => member.IsTester && member.Role == MembershipRole.Commissioner && member.LoginName == "michael");
        league.Members.Should().OnlyContain(member => member.Status == SubmissionStatus.NotStarted);

        SimSetView set = league.Set!;
        set.ActiveGames.Should().HaveCountGreaterThanOrEqualTo(8, "Top 25 plus SEC conference games");
        set.LockAtUtc.Should().Be(SimHarness.TuesdayAfternoonUtc.AddMinutes(15));
        set.IsFrozen.Should().BeFalse();

        snapshot.Games.Where(game => !game.IsEligible).Select(game => game.SetCount).Should().OnlyContain(count => count == 0,
            "the off-day game and the FCS game are left out by the app's own generator");
        set.Games.Should().Contain(game => game.PointValue > 10, "the close-spread and conference point rules elevate some games");
    }

    [Fact]
    public async Task GivenPicksForEveryone_WhenTheLockArrivesAndGamesPlayOut_ThenTheWeekIsScoredAndComplete()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        SimSnapshot snapshot = await sim.CreateSandboxAsync();

        SimHarness.Ok(await sim.World.FillEveryoneAsync(PickStrategy.Mixed, includeTester: true, submit: true, leaveOneIncomplete: true, CancellationToken.None));
        snapshot = await sim.SnapshotAsync();
        SimLeagueView league = snapshot.Leagues[0];
        league.Members.Count(member => member.Status == SubmissionStatus.Submitted).Should().Be(5);
        league.Members.Should().ContainSingle(member => member.Status == SubmissionStatus.InProgress);

        // Real time passes: the first kickoff arrives and autoplay runs the games.
        await sim.PassAsync(TimeSpan.FromMinutes(16));
        snapshot = await sim.SnapshotAsync();
        snapshot.Leagues[0].Set!.LockedUtc.Should().NotBeNull("autoplay locks what is due before it scores anything");
        snapshot.Leagues[0].Members.Should().OnlyContain(member => member.Status == SubmissionStatus.Locked || member.Status == SubmissionStatus.Incomplete);
        snapshot.Games.Should().Contain(game => game.Status == GameStatus.InProgress);

        for (int tick = 0; tick < 12; tick++)
        {
            await sim.PassAsync(TimeSpan.FromMinutes(10));
        }

        snapshot = await sim.SnapshotAsync();
        snapshot.Games.Where(game => game.IsEligible).Should().OnlyContain(game => game.Status == GameStatus.Final);
        snapshot.Leagues[0].Set!.IsComplete.Should().BeTrue();
        snapshot.Leagues[0].Members.Should().OnlyContain(member => member.WeekPoints != null);
        snapshot.Leagues[0].Members.Select(member => member.WeekPoints).Distinct().Should().HaveCountGreaterThan(1, "mixed picks spread the league out");

        int standings = await sim.CountAsync(context => context.SeasonStandingsSnapshots);
        standings.Should().Be(6, "a completed week writes a standings snapshot per member");
    }

    [Fact]
    public async Task GivenASandbox_WhenThreeWeeksArePlayedOut_ThenWeekFourIsReadyAndTheSeasonHasHistory()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        await sim.CreateSandboxAsync();

        SimHarness.Ok(await sim.World.PlayOutWeeksAsync(3, includeTester: true, CancellationToken.None));
        SimSnapshot snapshot = await sim.SnapshotAsync();

        snapshot.Calendar.Current!.Value.Week.Should().Be(4);
        snapshot.Leagues[0].Set.Should().NotBeNull();
        snapshot.Leagues[0].Set!.LockAtUtc.Should().BeAfter(sim.Clock.GetUtcNow());
        snapshot.Leagues[0].Members.Should().OnlyContain(member => member.Status == SubmissionStatus.NotStarted);
        snapshot.Leagues[0].Members.Should().OnlyContain(member => member.SeasonPoints > 0);

        List<int> completeWeeks = await sim.QueryAsync(context => context.WeekGameSets
            .Where(set => set.IsComplete).Select(set => set.Week).OrderBy(week => week).ToListAsync());
        completeWeeks.Should().Equal(1, 2, 3);

        List<int> throughWeeks = await sim.QueryAsync(context => context.SeasonStandingsSnapshots
            .Select(row => row.ThroughWeek).Distinct().OrderBy(week => week).ToListAsync());
        throughWeeks.Should().Equal([1, 2, 3], "two or more snapshots are what trend arrows compare");
    }

    [Fact]
    public async Task GivenALockedWeek_WhenRewound_ThenPicksStayStatusesReturnAndTheLockIsBackInTheFuture()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        await sim.CreateSandboxAsync();
        SimHarness.Ok(await sim.World.FillEveryoneAsync(PickStrategy.Favorites, includeTester: true, submit: true, leaveOneIncomplete: false, CancellationToken.None));
        SimHarness.Ok(await sim.World.LockNowAsync(CancellationToken.None));
        await sim.PassAsync(TimeSpan.FromMinutes(5));

        SimSnapshot locked = await sim.SnapshotAsync();
        locked.Leagues[0].Set!.LockedUtc.Should().NotBeNull();
        int picks = locked.Leagues[0].Members.Sum(member => member.Picks.Count);

        SimHarness.Ok(await sim.World.RewindWeekAsync(CancellationToken.None));
        SimSnapshot rewound = await sim.SnapshotAsync();

        rewound.Leagues[0].Set!.LockedUtc.Should().BeNull();
        rewound.Leagues[0].Set!.LockAtUtc.Should().Be(sim.Clock.GetUtcNow().AddMinutes(15));
        rewound.Leagues[0].Members.Should().OnlyContain(member => member.Status == SubmissionStatus.Submitted);
        rewound.Leagues[0].Members.Sum(member => member.Picks.Count).Should().Be(picks);
        rewound.Games.Should().OnlyContain(game => game.Status == GameStatus.Scheduled && game.HomeScore == null);
        (await sim.CountAsync(context => context.WeekResults)).Should().Be(0);
    }

    [Fact]
    public async Task GivenAWeek_WhenReset_ThenPicksAreGoneAndAFreshSetIsGenerated()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        await sim.CreateSandboxAsync();
        SimHarness.Ok(await sim.World.FillEveryoneAsync(PickStrategy.Mixed, includeTester: false, submit: true, leaveOneIncomplete: false, CancellationToken.None));

        SimHarness.Ok(await sim.World.ResetWeekAsync(CancellationToken.None));
        SimSnapshot snapshot = await sim.SnapshotAsync();

        snapshot.Leagues[0].Set.Should().NotBeNull();
        snapshot.Leagues[0].Set!.ActiveGames.Should().NotBeEmpty();
        snapshot.Leagues[0].Members.Should().OnlyContain(member => member.Picks.Count == 0 && member.Status == SubmissionStatus.NotStarted);
    }

    [Fact]
    public async Task GivenAnOpenSet_WhenAGameIsPostponed_ThenTheAppTakesItOutOfTheSet()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        SimSnapshot snapshot = await sim.CreateSandboxAsync();
        SimGameView game = snapshot.Games.First(candidate => candidate.SetCount == 1);

        SimHarness.Ok(await sim.World.SetDisruptionAsync(game.GameId, GameStatus.Postponed, CancellationToken.None));
        snapshot = await sim.SnapshotAsync();

        snapshot.Leagues[0].Set!.Games.Single(row => row.GameId == game.GameId).IsRemoved.Should().BeTrue();

        SimHarness.Ok(await sim.World.SetDisruptionAsync(game.GameId, GameStatus.Scheduled, CancellationToken.None));
        snapshot = await sim.SnapshotAsync();
        snapshot.Games.Single(row => row.GameId == game.GameId).Status.Should().Be(GameStatus.Scheduled);
    }

    [Fact]
    public async Task GivenAnOpenSet_WhenALineIsPostedAndTakenAway_ThenThePointValueFollowsIt()
    {
        const int DefaultPoints = 10;
        const int CloseSpreadPoints = 20; // the sandbox's rule: lines inside SlateBuilder.CloseSpread

        await using SimHarness sim = await SimHarness.StartAsync();
        SimSnapshot snapshot = await sim.CreateSandboxAsync();

        // Not a conference game, so only the close-spread rule can lift it off the default.
        SimGameView game = snapshot.Games.First(candidate => candidate.SetCount == 1 && !candidate.IsConferenceGame);

        // A minute between posts, so each new line is strictly the newest (the week locks in 15).
        async Task<int> PostLineAsync(decimal? spread)
        {
            sim.Clock.Advance(TimeSpan.FromMinutes(1));
            SimHarness.Ok(await sim.World.SetSpreadAsync(game.GameId, spread, CancellationToken.None));
            SimSnapshot current = await sim.SnapshotAsync();
            return current.Leagues[0].Set!.Games.Single(row => row.GameId == game.GameId).PointValue;
        }

        (await PostLineAsync(-10m)).Should().Be(DefaultPoints);
        (await PostLineAsync(-1.5m)).Should().Be(CloseSpreadPoints);
        (await PostLineAsync(null)).Should().Be(DefaultPoints);
    }

    [Fact]
    public async Task GivenAScoredWeek_WhenAGameIsReopened_ThenTheWeekIsNoLongerComplete()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        await sim.CreateSandboxAsync();
        SimHarness.Ok(await sim.World.PlayOutWeeksAsync(1, includeTester: true, CancellationToken.None));

        // Back to week 1, which is complete.
        SimHarness.Ok(await sim.World.StartWeekAsync(1, CancellationToken.None));
        SimSnapshot snapshot = await sim.SnapshotAsync();
        snapshot.Leagues[0].Set!.IsComplete.Should().BeTrue();

        SimGameView game = snapshot.Games.First(candidate => candidate.SetCount == 1);
        SimHarness.Ok(await sim.World.ReopenAsync(game.GameId, CancellationToken.None));

        snapshot = await sim.SnapshotAsync();
        snapshot.Games.Single(row => row.GameId == game.GameId).Status.Should().Be(GameStatus.InProgress);
        snapshot.Leagues[0].Set!.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GivenATieFinal_WhenScored_ThenNobodyGetsThePointsAndTheWeekStaysOpen()
    {
        await using SimHarness sim = await SimHarness.StartAsync();
        await sim.CreateSandboxAsync();
        SimHarness.Ok(await sim.World.FillEveryoneAsync(PickStrategy.HomeTeams, includeTester: true, submit: true, leaveOneIncomplete: false, CancellationToken.None));
        SimHarness.Ok(await sim.World.LockNowAsync(CancellationToken.None));

        SimSnapshot snapshot = await sim.SnapshotAsync();
        Guid tied = snapshot.Games.First(candidate => candidate.SetCount == 1).GameId;
        SimHarness.Ok(await sim.World.SetScoreAsync(tied, 17, 17, final: true, CancellationToken.None));
        SimHarness.Ok(await sim.World.FinishStartedAsync(CancellationToken.None));
        await sim.PassAsync(TimeSpan.FromHours(3));

        snapshot = await sim.SnapshotAsync();
        snapshot.Games.Single(game => game.GameId == tied).Should().Match<SimGameView>(game => game.HomeScore == 17 && game.AwayScore == 17);
        snapshot.Leagues[0].Set!.IsComplete.Should().BeFalse("a tie has no winner until a commissioner overrides or voids it");
    }

    [Fact]
    public async Task GivenAWeekThatCrossesMidnightEastern_WhenPlayedOutByAutoplay_ThenEveryGameIsFinal()
    {
        // 11:50 PM Eastern: the first kickoff is after midnight, the off-day game before it.
        await using SimHarness sim = await SimHarness.StartAsync(new DateTimeOffset(2026, 10, 7, 3, 50, 0, TimeSpan.Zero));
        await sim.CreateSandboxAsync();

        for (int tick = 0; tick < 20; tick++)
        {
            await sim.PassAsync(TimeSpan.FromMinutes(10));
        }

        SimSnapshot snapshot = await sim.SnapshotAsync();
        snapshot.Games.Should().OnlyContain(game => game.Status == GameStatus.Final);
        snapshot.Games.Select(game => DateOnly.FromDateTime(Domain.Seasons.SeasonCalendar.ToEastern(game.KickoffUtc).DateTime))
            .Distinct().Should().HaveCountGreaterThan(1, "the slate really does span two Eastern dates");
    }

    [Fact]
    public async Task GivenADatabaseSomebodyElseOwns_WhenTheSimulatorStarts_ThenItRefusesToTouchIt()
    {
        SimTestDatabase database = await SimTestDatabase.CreateAsync();
        await using (AppDbContext context = database.CreateContext())
        {
            context.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                ExternalSubject = "someone@github",
                Email = "someone@example.com",
                DisplayName = "Someone",
                CreatedUtc = DateTime.UtcNow,
                LastLoginUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        await using SimHarness sim = await SimHarness.StartAsync(database: database);

        sim.Status.IsUsable.Should().BeFalse();
        sim.Status.Problem.Should().Contain("was not created by the simulator");

        SimLog log = await sim.World.CreateSandboxAsync(new SandboxRequest(), CancellationToken.None);
        log.Failed.Should().BeTrue();
        (await sim.CountAsync(context => context.Leagues)).Should().Be(0);
    }
}
