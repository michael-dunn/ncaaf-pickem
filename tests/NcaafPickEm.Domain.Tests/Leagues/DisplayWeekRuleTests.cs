using System.Globalization;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Domain.Tests.Leagues;

/// <summary>
/// Feature 15 (D-193, 04-Domain-Algorithms.md section 12.1): the display week is the week just
/// played from the Sunday rollover until the next week's picks open (Tuesday 04:00 ET), and the
/// calendar's current week otherwise.
/// </summary>
public sealed class DisplayWeekRuleTests
{
    private const int FirstWeek = 1;
    private const int LastWeek = 14;

    // Week 7 of 2026 runs Sun 11 Oct to Sat 17 Oct ET (EDT); week 8 from Sun 18 Oct, its picks open
    // Tue 20 Oct 04:00 EDT = 08:00 UTC. Week 10 starts on the fall-back Sunday, 1 Nov.
    private static readonly IReadOnlyList<SeasonWeek> Weeks = BuildWeeks();

    private static readonly int[] PlayedThrough7 = [1, 2, 3, 4, 5, 6, 7];

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

    private static int Display(
        string nowUtc,
        IReadOnlyCollection<int> lockedWeeks,
        int firstWeek = FirstWeek,
        int lastWeek = LastWeek) =>
        DisplayWeekRule.At(
            DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture), Weeks, firstWeek, lastWeek, lockedWeeks);

    [Theory]
    [InlineData("2026-10-14T12:00:00Z")] // Wednesday of week 7
    [InlineData("2026-10-17T18:00:00Z")] // Saturday during the games (week 7 locked at noon)
    [InlineData("2026-10-18T03:30:00Z")] // Saturday 23:30 ET, every game final
    public void GivenWeek7sOwnWindowAfterItsPicksOpened_ThenTheDisplayWeekIs7(string nowUtc)
    {
        Display(nowUtc, PlayedThrough7).Should().Be(7);
    }

    [Theory]
    [InlineData("2026-10-18T04:00:00Z")] // Sunday 00:00 ET: the calendar rolls to week 8
    [InlineData("2026-10-20T03:59:59Z")] // Monday 23:59:59 ET
    [InlineData("2026-10-20T07:59:59Z")] // Tuesday 03:59:59 ET
    public void GivenTheResultsWindow_ThenTheDisplayWeekIsTheWeekJustPlayed(string nowUtc)
    {
        Display(nowUtc, PlayedThrough7).Should().Be(7);
    }

    [Fact]
    public void GivenTheNextWeeksPicksOpenMoment_ThenTheDisplayWeekMovesOn()
    {
        Display("2026-10-20T08:00:00Z", PlayedThrough7).Should().Be(8);
    }

    [Fact]
    public void GivenAWeekStillBeingPlayedOnSunday_ThenTheResultsWindowStillShowsIt()
    {
        // A postponed or late game keeps week 7 In Progress; it is locked, so it is still the week shown.
        Display("2026-10-18T16:00:00Z", PlayedThrough7).Should().Be(7);
    }

    [Fact]
    public void GivenTheLeaguesFirstWeek_ThenThereIsNoPreviousWeekToShow()
    {
        // League starts at week 8: nothing locked before it, so Sunday and Monday show week 8.
        Display("2026-10-19T12:00:00Z", lockedWeeks: [], firstWeek: 8).Should().Be(8);

        // A week before the league's range that happens to be locked never counts.
        Display("2026-10-19T12:00:00Z", lockedWeeks: [7], firstWeek: 8).Should().Be(8);
    }

    [Fact]
    public void GivenAByeWeekBeforeTheCurrentOne_ThenTheMostRecentPlayedWeekIsShown()
    {
        // Week 7 had no set (nothing locked); on the Monday of week 8 the display week is week 6.
        Display("2026-10-19T12:00:00Z", lockedWeeks: [5, 6]).Should().Be(6);
    }

    [Fact]
    public void GivenTheLeaguesLastWeekHasBeenPlayed_ThenTheDisplayWeekStaysOnIt()
    {
        // A league ending at week 7: from Sunday of week 8 on, week 7 for good.
        Display("2026-10-19T12:00:00Z", PlayedThrough7, lastWeek: 7).Should().Be(7);
        Display("2026-10-28T12:00:00Z", PlayedThrough7, lastWeek: 7).Should().Be(7);
    }

    [Fact]
    public void GivenBeforeTheLeaguesFirstWeek_ThenTheDisplayWeekIsTheFirstWeek()
    {
        Display("2026-10-14T12:00:00Z", lockedWeeks: [], firstWeek: 9).Should().Be(9);
    }

    [Fact]
    public void GivenTheFallBackSunday_ThenTheResultsWindowRunsToTuesdayFourEasternStandardTime()
    {
        int[] playedThrough9 = [.. Enumerable.Range(1, 9)];

        Display("2026-11-01T05:30:00Z", playedThrough9).Should().Be(9); // Sun 01:30 EDT (first pass)
        Display("2026-11-01T06:30:00Z", playedThrough9).Should().Be(9); // Sun 01:30 EST (second pass)
        Display("2026-11-03T08:59:59Z", playedThrough9).Should().Be(9); // Tue 03:59:59 EST
        Display("2026-11-03T09:00:00Z", playedThrough9).Should().Be(10); // Tue 04:00 EST
    }

    [Fact]
    public void GivenNoCalendar_ThenTheDisplayWeekIsTheFirstWeek()
    {
        DisplayWeekRule.At(DateTimeOffset.UtcNow, [], 3, 14, [3, 4]).Should().Be(3);
    }
}
