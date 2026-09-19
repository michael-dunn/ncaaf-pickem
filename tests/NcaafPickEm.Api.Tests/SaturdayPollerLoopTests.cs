using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// What <see cref="SaturdayPoller.EvaluateOnceAsync"/> does per tick, as opposed to what
/// <see cref="SaturdayPollerSchedule"/> decides: the once-a-minute loop must call the provider
/// only on the active source's cadence, must always ask for the window's <em>Saturday</em>
/// Eastern date (including after midnight), and must leave a failed poll on
/// <c>DataRefreshStatus(Scores)</c> (04-Domain-Algorithms.md section 10).
/// </summary>
public sealed class SaturdayPollerLoopTests
{
    /// <summary>The fixture week's Saturday; every Week 7, 2026 kickoff is on this Eastern date.</summary>
    private static readonly DateOnly FixtureSaturday = new(2026, 10, 17);

    [Fact]
    public async Task GivenAnOpenWindow_WhenTheLoopTicksEveryMinute_ThenEspnIsCalledOnlyEveryFiveMinutes()
    {
        var provider = new RecordingLiveScoreProvider();
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);

        await using ApiFactory factory = CreateFactory(testDatabase, clock, provider);
        DateTimeOffset windowStart = await SeedWindowAsync(factory);

        SaturdayPoller poller = CreatePoller(factory, clock);

        // 15 consecutive one-minute ticks from the instant the window opens.
        for (int minute = 0; minute < 15; minute++)
        {
            clock.Set(windowStart.AddMinutes(minute));
            await poller.EvaluateOnceAsync(CancellationToken.None);
        }

        provider.Calls.Should().HaveCount(
            3,
            "ESPN's cadence is five minutes, so fifteen minutes of one-minute ticks is three calls, not fifteen");
    }

    [Fact]
    public async Task GivenTheActiveSourceIsCfbd_WhenTheLoopTicksEveryMinute_ThenItIsCalledOnlyEveryTenMinutes()
    {
        var provider = new RecordingLiveScoreProvider();
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);

        await using ApiFactory factory = CreateFactory(testDatabase, clock, provider);
        DateTimeOffset windowStart = await SeedWindowAsync(factory);

        SaturdayPoller poller = CreatePoller(factory, clock, new CfbdActiveHealth());

        for (int minute = 0; minute < 20; minute++)
        {
            clock.Set(windowStart.AddMinutes(minute));
            await poller.EvaluateOnceAsync(CancellationToken.None);
        }

        provider.Calls.Should().HaveCount(2, "the CFBD fallback polls every ten minutes");
    }

    [Fact]
    public async Task GivenAPollAfterMidnightEastern_WhenTheLoopTicks_ThenItStillAsksForTheWindowsSaturday()
    {
        var provider = new RecordingLiveScoreProvider();
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);

        await using ApiFactory factory = CreateFactory(testDatabase, clock, provider);
        await SeedWindowAsync(factory);

        // Sunday 2026-10-18 01:30 ET (EDT, UTC-4) = 05:30 UTC: inside the window, past midnight.
        clock.Set(SeasonCalendar.ToUtc(new DateTime(2026, 10, 18, 1, 30, 0)));

        SaturdayPoller poller = CreatePoller(factory, clock);
        await poller.EvaluateOnceAsync(CancellationToken.None);

        provider.Calls.Should().ContainSingle().Which.Should().Be(
            FixtureSaturday,
            "ESPN buckets a 22:30 ET kickoff's post-midnight final on the Saturday date, so the "
            + "window must keep asking for that date rather than for 'today'");
    }

    [Fact]
    public async Task GivenTheProviderThrows_WhenTheLoopTicks_ThenTheFailureLandsOnDataRefreshStatus()
    {
        var provider = new RecordingLiveScoreProvider { Throw = new InvalidOperationException("ESPN is down") };
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);

        await using ApiFactory factory = CreateFactory(testDatabase, clock, provider);
        DateTimeOffset windowStart = await SeedWindowAsync(factory);
        clock.Set(windowStart);

        SaturdayPoller poller = CreatePoller(factory, clock);

        await FluentActions.Awaiting(() => poller.EvaluateOnceAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>(
                "the hosted loop's own guard is what swallows it, so the evaluation must not hide it");

        DataRefreshStatus? status = await factory.QueryDbAsync(database => database.DataRefreshStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.DataType == RefreshDataType.Scores));

        status.Should().NotBeNull();
        status!.LastError.Should().Contain("ESPN is down");
        status.LastSuccessUtc.Should().BeNull();
    }

    private static ApiFactory CreateFactory(
        SqlTestDatabase testDatabase,
        TimeProvider clock,
        ILiveScoreProvider provider) =>
        new(
            testDatabase.ConnectionString,
            timeProvider: clock,
            configureServices: services =>
            {
                services.RemoveAll<ILiveScoreProvider>();
                services.AddSingleton(provider);
            });

    private static SaturdayPoller CreatePoller(
        ApiFactory factory,
        TimeProvider clock,
        ILiveScoreHealth? health = null) =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            health ?? factory.Services.GetRequiredService<ILiveScoreHealth>(),
            Options.Create(new JobsOptions()),
            NullLogger<SaturdayPoller>.Instance);

    /// <summary>
    /// Seeds a league whose current-week set holds the fixture's Saturday games, all still
    /// <see cref="GameStatus.Scheduled"/>, and returns the instant the poller's window opens
    /// (<c>min(LockAtUtc) - 5 min</c>).
    /// </summary>
    private static async Task<DateTimeOffset> SeedWindowAsync(ApiFactory factory)
    {
        DateTime lockAtUtc = await factory.QueryDbAsync(async database =>
        {
            List<Game> games = await database.Games
                .Include(game => game.HomeTeam)
                .Include(game => game.AwayTeam)
                .Where(game => game.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear
                    && game.Week == 7
                    && game.IsSaturdayEastern
                    && game.HomeTeam!.Classification == TeamClassification.Fbs
                    && game.AwayTeam!.Classification == TeamClassification.Fbs)
                .ToListAsync();

            Domain.Users.User owner = await TestUsers.CreateUserAsync(database);

            var league = new League
            {
                Id = Guid.CreateVersion7(),
                Name = $"Poller Loop League {Guid.CreateVersion7().ToString()[..8]}",
                SeasonYear = FixtureSeasonWeekSource.FixtureSeasonYear,
                FirstWeek = 1,
                LastWeek = 14,
                DefaultPointValue = 10,
                CreatedByUserId = owner.Id,
                CreatedUtc = DateTime.UtcNow,
            };

            var set = new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = 7,
                GeneratedUtc = DateTime.UtcNow,
                LockAtUtc = games.Min(game => game.KickoffUtc),
            };

            database.Leagues.Add(league);
            database.WeekGameSets.Add(set);

            foreach (Game game in games)
            {
                database.WeekGameSetGames.Add(new WeekGameSetGame
                {
                    Id = Guid.CreateVersion7(),
                    WeekGameSetId = set.Id,
                    GameId = game.Id,
                    Source = GameSetGameSource.Rule,
                    ResolvedPointValue = 10,
                });
            }

            await database.SaveChangesAsync();

            return set.LockAtUtc!.Value;
        });

        return new DateTimeOffset(DateTime.SpecifyKind(lockAtUtc, DateTimeKind.Utc))
            .AddMinutes(-SaturdayPollerSchedule.WindowOpensBeforeLockMinutes);
    }

    /// <summary>Records every Eastern date the poller asked for, and optionally fails.</summary>
    private sealed class RecordingLiveScoreProvider : ILiveScoreProvider
    {
        public List<DateOnly> Calls { get; } = [];

        public Exception? Throw { get; init; }

        public Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
            DateOnly easternDate,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(easternDate);

            return Throw is not null
                ? Task.FromException<IReadOnlyList<LiveScoreUpdate>>(Throw)
                : Task.FromResult<IReadOnlyList<LiveScoreUpdate>>([]);
        }
    }

    /// <summary>A health that reports the CFBD fallback as active, without engaging it for real.</summary>
    private sealed class CfbdActiveHealth : ILiveScoreHealth
    {
        public LiveScoreSource ConfiguredSource => LiveScoreSource.Espn;

        public LiveScoreSource ActiveSource => LiveScoreSource.Cfbd;

        public int ConsecutiveFailures => 0;

        public bool ScoresMayBeStale => true;

        public void RecordSuccess()
        {
        }

        public void RecordFailure()
        {
        }

        public void ResetForNewDay()
        {
        }
    }
}
