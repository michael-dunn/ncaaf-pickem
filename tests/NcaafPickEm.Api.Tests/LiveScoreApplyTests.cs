using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="LiveScoreApplyService"/> (P2-03) driven through the Week 7, 2026 fixture snapshots:
/// status, scores, period and clock reach <c>Games</c>; <c>GameWentFinal</c> fires exactly once
/// per game; a tie produces no winner; the post-midnight finish still belongs to week 7;
/// re-applying a snapshot is a no-op.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LiveScoreApplyTests
{
    /// <summary>The fixture week's Saturday. Every snapshot event kicks off on this Eastern date.</summary>
    private static readonly DateOnly Saturday = new(2026, 10, 17);

    private static readonly DateOnly Sunday = new(2026, 10, 18);

    private readonly ApiTestFixture _fixture;

    public LiveScoreApplyTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheSixSnapshotsInOrder_WhenApplied_ThenEveryGameGoesFinalExactlyOnce()
    {
        await ResetAsync();

        List<GameWentFinal> finals = [];
        for (int snapshot = FixtureSnapshotState.MinSnapshot; snapshot <= FixtureSnapshotState.MaxSnapshot; snapshot++)
        {
            LiveScoreApplyResult result = await ApplyAsync(Saturday, await SnapshotAsync(snapshot));
            finals.AddRange(result.Events.OfType<GameWentFinal>());
        }

        // The 15 games in the score timeline (the 16th is the Friday game, which no Saturday
        // payload contains).
        finals.Should().HaveCount(15);
        finals.Select(f => f.GameId).Should().OnlyHaveUniqueItems();
        finals.Should().OnlyContain(f => f.SeasonYear == 2026 && f.Week == 7);

        // Re-polling once everything is final raises nothing and writes nothing.
        LiveScoreApplyResult replay = await ApplyAsync(Saturday, await SnapshotAsync(6));
        replay.Events.Should().BeEmpty();
        replay.Changed.Should().Be(0);
        replay.Matched.Should().Be(15);
    }

    [Fact]
    public async Task GivenTheFinalTieGame_WhenItGoesFinal_ThenNoWinnerIsDetermined()
    {
        await ResetAsync();

        LiveScoreApplyResult result = await ApplyAsync(Saturday, await SnapshotAsync(5));

        Game tie = await GameAsync(700011); // Iowa State 24 - Kansas 24.
        tie.Status.Should().Be(GameStatus.Final);
        tie.HomeScore.Should().Be(24);
        tie.AwayScore.Should().Be(24);

        GameWentFinal wentFinal = result.Events.OfType<GameWentFinal>().Single(e => e.GameId == tie.Id);
        wentFinal.WinnerTeamId.Should().BeNull();
    }

    [Fact]
    public async Task GivenAFinishedGame_WhenItGoesFinal_ThenTheHigherScoreWins()
    {
        await ResetAsync();

        await ApplyAsync(Saturday, await SnapshotAsync(5));

        Game game = await GameAsync(700001); // Michigan 27 - Texas 24.
        game.Status.Should().Be(GameStatus.Final);
        game.HomeScore.Should().Be(27);
        game.AwayScore.Should().Be(24);
        game.Period.Should().BeNull();
        game.Clock.Should().BeNull();

        Game awayWin = await GameAsync(700008); // Oklahoma 27 - Missouri 30.
        awayWin.AwayScore.Should().Be(30);
    }

    [Fact]
    public async Task GivenTheLateGameFinishingAfterMidnightEastern_WhenPolledOnSunday_ThenItStillScoresAgainstWeekSeven()
    {
        await ResetAsync();

        for (int snapshot = 1; snapshot <= 5; snapshot++)
        {
            await ApplyAsync(Saturday, await SnapshotAsync(snapshot));
        }

        Game late = await GameAsync(700004); // San José State / Hawai'i, 22:30 ET kickoff.
        late.Status.Should().Be(GameStatus.InProgress);

        // Snapshot 6 is stamped 2026-10-18T05:45Z = 01:45 ET Sunday. ESPN would still return it
        // under Saturday's date, but a poller tick that asks for "today" gets Sunday - and the
        // candidate window is what makes that work.
        LiveScoreApplyResult result = await ApplyAsync(Sunday, await SnapshotAsync(6));

        late = await GameAsync(700004);
        late.Status.Should().Be(GameStatus.Final);
        late.HomeScore.Should().Be(31);
        late.AwayScore.Should().Be(28);

        GameWentFinal wentFinal = result.Events.OfType<GameWentFinal>().Single(e => e.GameId == late.Id);
        wentFinal.Week.Should().Be(7);
        wentFinal.WinnerTeamId.Should().Be(late.HomeTeamId);
    }

    [Fact]
    public async Task GivenAnInProgressSnapshot_WhenApplied_ThenPeriodClockAndProviderIdsArePersisted()
    {
        await ResetAsync();

        await ApplyAsync(Saturday, await SnapshotAsync(3));

        Game game = await GameAsync(700001);
        game.Status.Should().Be(GameStatus.InProgress);
        game.EspnEventId.Should().Be(401900000);
        game.Period.Should().NotBeNull();
        game.Clock.Should().NotBeNullOrWhiteSpace();
        game.LastScoreUpdateUtc.Should().NotBeNull();

        Team home = await TeamAsync(game.HomeTeamId);
        Team away = await TeamAsync(game.AwayTeamId);
        home.EspnTeamId.Should().Be(80101);
        away.EspnTeamId.Should().Be(80102);
    }

    [Fact]
    public async Task GivenAFinalGame_WhenAStalePayloadSaysInProgress_ThenTheFinalStands()
    {
        await ResetAsync();
        await ApplyAsync(Saturday, await SnapshotAsync(5));

        Game before = await GameAsync(700001);
        LiveScoreUpdate stale = (await SnapshotAsync(5)).Single(u => u.SourceEventId == "401900000")
            with
        { Status = GameStatus.InProgress, HomeScore = 3, AwayScore = 0, Period = 1, Clock = "10:00" };

        LiveScoreApplyResult result = await ApplyAsync(Saturday, [stale]);

        Game after = await GameAsync(700001);
        after.Status.Should().Be(GameStatus.Final);
        after.HomeScore.Should().Be(before.HomeScore);
        after.AwayScore.Should().Be(before.AwayScore);
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenAPostponedThenReinstatedGame_WhenApplied_ThenGameScheduleChangedFiresBothWays()
    {
        await ResetAsync();

        LiveScoreUpdate scheduled = (await SnapshotAsync(1)).Single(u => u.SourceEventId == "401900001");
        LiveScoreUpdate postponed = scheduled with { Status = GameStatus.Postponed, RawStatusName = "STATUS_POSTPONED" };

        LiveScoreApplyResult postponement = await ApplyAsync(Saturday, [postponed]);

        GameScheduleChanged changed = postponement.Events.OfType<GameScheduleChanged>().Single();
        changed.OldStatus.Should().Be(GameStatus.Scheduled);
        changed.NewStatus.Should().Be(GameStatus.Postponed);
        (await GameAsync(700002)).Status.Should().Be(GameStatus.Postponed);

        // Applying it again is a no-op: the transition already happened.
        (await ApplyAsync(Saturday, [postponed])).Events.Should().BeEmpty();

        // And the game can come back.
        LiveScoreApplyResult reinstated = await ApplyAsync(Saturday, [scheduled]);
        GameScheduleChanged back = reinstated.Events.OfType<GameScheduleChanged>().Single();
        back.OldStatus.Should().Be(GameStatus.Postponed);
        back.NewStatus.Should().Be(GameStatus.Scheduled);
    }

    [Fact]
    public async Task GivenAnUnmatchedFbsPair_WhenAppliedTwice_ThenExactlyOneUnmatchedRowIsWritten()
    {
        await ResetAsync();

        LiveScoreUpdate template = (await SnapshotAsync(1)).Single(u => u.SourceEventId == "401900000");
        LiveScoreUpdate unmatched = template with
        {
            SourceEventId = "401999999",
            HomeName = "Texas",
            HomeAbbreviation = "TEX",
            AwayName = "Baylor",
            AwayAbbreviation = "BAY",
        };

        LiveScoreApplyResult first = await ApplyAsync(Saturday, [unmatched, unmatched]);
        LiveScoreApplyResult second = await ApplyAsync(Saturday, [unmatched]);

        first.Unmatched.Should().Be(1);
        second.Unmatched.Should().Be(0);

        UnmatchedGame[] rows = await QueryAsync(db => db.UnmatchedGames
            .Where(u => u.RawHomeName == "Texas" && u.RawAwayName == "Baylor")
            .ToArrayAsync());

        rows.Should().ContainSingle();
        rows[0].Source.Should().Be(ProviderSource.Espn);
        rows[0].GameDate.Should().Be(Saturday);
        rows[0].RawPayload.Should().Contain("401999999");
    }

    [Fact]
    public async Task GivenAnFcsOnlyEvent_WhenApplied_ThenItIsIgnoredWithoutAnUnmatchedRow()
    {
        await ResetAsync();

        LiveScoreUpdate template = (await SnapshotAsync(1)).Single(u => u.SourceEventId == "401900000");
        LiveScoreUpdate noise = template with
        {
            SourceEventId = "401999998",
            HomeName = "Gardner-Webb",
            HomeAbbreviation = "GWEB",
            AwayName = "Stonehill",
            AwayAbbreviation = "STO",
        };

        LiveScoreApplyResult result = await ApplyAsync(Saturday, [noise]);

        result.Ignored.Should().Be(1);
        result.Unmatched.Should().Be(0);
        (await QueryAsync(db => db.UnmatchedGames.CountAsync(u => u.RawHomeName == "Gardner-Webb")))
            .Should().Be(0);
    }

    private static async Task<IReadOnlyList<LiveScoreUpdate>> SnapshotAsync(int snapshot)
    {
        var state = new FixtureSnapshotState();
        state.Set(snapshot);
        return await new FixtureLiveScoreProvider(state).GetScoresAsync(Saturday);
    }

    private async Task<LiveScoreApplyResult> ApplyAsync(DateOnly easternDate, IReadOnlyList<LiveScoreUpdate> updates)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        LiveScoreApplyService service = scope.ServiceProvider.GetRequiredService<LiveScoreApplyService>();
        return await service.ApplyAsync(easternDate, updates);
    }

    private Task<Game> GameAsync(long cfbdGameId) =>
        QueryAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.CfbdGameId == cfbdGameId));

    private Task<Team> TeamAsync(Guid teamId) =>
        QueryAsync(db => db.Teams.AsNoTracking().SingleAsync(t => t.Id == teamId));

    private async Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>
    /// Puts the fixture week back to kickoff. The API suite shares one database, so every test
    /// here starts from Scheduled rows with nothing learned from a previous run.
    /// </summary>
    private async Task ResetAsync()
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await scope.ServiceProvider.GetRequiredService<FixtureSeeder>().SeedAsync(seedDemoLeague: false);

        await database.Games.ExecuteUpdateAsync(setters => setters
            .SetProperty(game => game.Status, GameStatus.Scheduled)
            .SetProperty(game => game.HomeScore, (int?)null)
            .SetProperty(game => game.AwayScore, (int?)null)
            .SetProperty(game => game.Period, (byte?)null)
            .SetProperty(game => game.Clock, (string?)null)
            .SetProperty(game => game.EspnEventId, (long?)null)
            .SetProperty(game => game.LastScoreUpdateUtc, (DateTime?)null));

        await database.Teams.ExecuteUpdateAsync(setters => setters.SetProperty(team => team.EspnTeamId, (int?)null));
        await database.UnmatchedGames.ExecuteDeleteAsync();
    }
}
