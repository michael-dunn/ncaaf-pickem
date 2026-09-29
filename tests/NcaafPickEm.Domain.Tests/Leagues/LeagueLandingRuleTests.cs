using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Leagues;

/// <summary>
/// 04-Domain-Algorithms.md section 12 (D-187): the screen the app opens a league on. Picks while
/// the week is open, the Dashboard once it locks, the Leaderboard once its games are done and on
/// the Sunday after a played week.
/// </summary>
public sealed class LeagueLandingRuleTests
{
    private const int FirstWeek = 1;
    private const int LastWeek = 14;

    // Week 7 of 2026 runs Sun 11 Oct to Sat 17 Oct ET (EDT, UTC-4); Week 8 starts Sun 18 Oct.
    private static readonly DateTimeOffset Week7Wednesday = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Week7LockAt = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Week7SaturdayAfternoon = new(2026, 10, 17, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Week7SaturdayLateNight = new(2026, 10, 18, 3, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Week8SundayNoon = new(2026, 10, 18, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Week8MondayMidnight = new(2026, 10, 19, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Week8LockAt = new(2026, 10, 24, 16, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<SeasonWeek> Weeks = BuildWeeks();

    private static IReadOnlyList<SeasonWeek> BuildWeeks()
    {
        DateOnly weekOneSaturday = new(2026, 9, 5);
        List<SeasonWeek> weeks = [];
        for (int week = 0; week <= 15; week++)
        {
            (DateTimeOffset startUtc, DateTimeOffset endUtc) = SeasonCalendar.WeekWindow(weekOneSaturday.AddDays(7 * (week - 1)));
            weeks.Add(new SeasonWeek(2026, week, startUtc, endUtc, IsRegularSeason: week < 15));
        }

        return weeks;
    }

    private static WeekGameSet Set(int week, DateTime? lockAtUtc, DateTime? lockedUtc = null, bool isComplete = false) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Week = week,
            LockAtUtc = lockAtUtc,
            LockedUtc = lockedUtc,
            IsComplete = isComplete,
        };

    private static WeekGameSet PlayedWeek7() => Set(7, Week7LockAt, lockedUtc: Week7LockAt, isComplete: true);

    private static WeekGameSet OpenWeek8() => Set(8, Week8LockAt);

    private static LeagueLanding Land(
        DateTimeOffset nowUtc,
        WeekGameSet? currentSet,
        WeekGameSet? previousSet,
        int firstWeek = FirstWeek,
        int lastWeek = LastWeek,
        IReadOnlyList<SeasonWeek>? weeks = null) =>
        LeagueLandingRule.Choose(nowUtc, weeks ?? Weeks, firstWeek, lastWeek, currentSet, previousSet);

    [Fact]
    public void GivenAWeekThatHasNotLocked_WhenOpeningTheLeague_ThenItLandsOnPicks()
    {
        WeekGameSet week6 = Set(6, Week7LockAt.AddDays(-7), lockedUtc: Week7LockAt.AddDays(-7), isComplete: true);

        Land(Week7Wednesday, Set(7, Week7LockAt), week6).Should().Be(LeagueLanding.Picks);
    }

    [Fact]
    public void GivenAWeekWithNoGameSetYet_WhenOpeningTheLeague_ThenItLandsOnPicks()
    {
        Land(Week7Wednesday, currentSet: null, previousSet: null).Should().Be(LeagueLanding.Picks);
        Land(Week7Wednesday, Set(7, lockAtUtc: null), previousSet: null).Should().Be(LeagueLanding.Picks);
    }

    [Fact]
    public void GivenTheLockInstantHasPassedBeforeTheLockJobRan_WhenOpeningTheLeague_ThenItLandsOnTheDashboard()
    {
        // Picks already refuse at this instant (WeekGameSetLockGuard), so Picks would be a dead end.
        DateTimeOffset justAfterLock = new DateTimeOffset(Week7LockAt).AddSeconds(30);

        Land(justAfterLock, Set(7, Week7LockAt), previousSet: null).Should().Be(LeagueLanding.Dashboard);
    }

    [Fact]
    public void GivenALockedWeekWithGamesStillBeingPlayed_WhenOpeningTheLeague_ThenItLandsOnTheDashboard()
    {
        WeekGameSet week7 = Set(7, Week7LockAt, lockedUtc: Week7LockAt, isComplete: false);

        Land(Week7SaturdayAfternoon, week7, previousSet: null).Should().Be(LeagueLanding.Dashboard);
    }

    [Fact]
    public void GivenALockedWeekWhoseGamesAreAllDone_WhenOpeningTheLeague_ThenItLandsOnTheLeaderboard()
    {
        Land(Week7SaturdayLateNight, PlayedWeek7(), previousSet: null).Should().Be(LeagueLanding.Leaderboard);
    }

    [Fact]
    public void GivenTheSundayAfterAPlayedWeek_WhenOpeningTheLeague_ThenItLandsOnTheLeaderboard()
    {
        // Week 8 is open for picks, but Sunday is for the weekend's results - even with a
        // delayed Week 7 game still being scored.
        WeekGameSet week7StillScoring = Set(7, Week7LockAt, lockedUtc: Week7LockAt, isComplete: false);

        Land(Week8SundayNoon, OpenWeek8(), PlayedWeek7()).Should().Be(LeagueLanding.Leaderboard);
        Land(Week8SundayNoon, OpenWeek8(), week7StillScoring).Should().Be(LeagueLanding.Leaderboard);
        Land(Week8SundayNoon, currentSet: null, PlayedWeek7()).Should().Be(LeagueLanding.Leaderboard);
    }

    [Fact]
    public void GivenTheMondayAfterAPlayedWeek_WhenOpeningTheLeague_ThenItLandsOnPicks()
    {
        Land(Week8MondayMidnight, OpenWeek8(), PlayedWeek7()).Should().Be(LeagueLanding.Picks);
    }

    [Fact]
    public void GivenTheSundayOfTheLeaguesFirstWeek_WhenOpeningTheLeague_ThenItLandsOnPicks()
    {
        // Nothing was played the week before: no set at all, or a row that never locked.
        Land(Week8SundayNoon, OpenWeek8(), previousSet: null, firstWeek: 8).Should().Be(LeagueLanding.Picks);
        Land(Week8SundayNoon, OpenWeek8(), Set(7, lockAtUtc: null)).Should().Be(LeagueLanding.Picks);
    }

    [Fact]
    public void GivenTheCalendarHasNotReachedTheLeaguesFirstWeek_WhenOpeningTheLeague_ThenItLandsOnTheLeagueHome()
    {
        // The league home says "Season starts Week 9 on <date>"; the picks page is unavailable.
        Land(Week7Wednesday, currentSet: null, previousSet: null, firstWeek: 9).Should().Be(LeagueLanding.LeagueHome);
    }

    [Fact]
    public void GivenTheLeaguesLastWeekHasClosed_WhenOpeningTheLeague_ThenItLandsOnTheLeaderboard()
    {
        // A league that ended at Week 7: on Monday of Week 8 the season leaderboard is final.
        Land(Week8MondayMidnight, currentSet: null, PlayedWeek7(), lastWeek: 7).Should().Be(LeagueLanding.Leaderboard);
    }

    [Fact]
    public void GivenNoCalendarYet_WhenOpeningTheLeague_ThenTheSetAloneDecides()
    {
        WeekGameSet lockedWeek1 = Set(1, Week7LockAt, lockedUtc: Week7LockAt);

        Land(Week7Wednesday, Set(1, Week7LockAt), previousSet: null, weeks: []).Should().Be(LeagueLanding.Picks);
        Land(Week7SaturdayAfternoon, lockedWeek1, previousSet: null, weeks: []).Should().Be(LeagueLanding.Dashboard);
    }
}
