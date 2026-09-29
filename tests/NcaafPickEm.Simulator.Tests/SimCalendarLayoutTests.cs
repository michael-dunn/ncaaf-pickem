using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Simulator.Engine;

namespace NcaafPickEm.Simulator.Tests;

/// <summary>
/// <see cref="SimCalendarLayout"/>: the calendar follows the tester. Whatever day it is, the week
/// under test contains now, and the app's own <see cref="SeasonCalendar.CurrentWeekAt"/> agrees.
/// </summary>
public sealed class SimCalendarLayoutTests
{
    private static readonly DateTimeOffset Tuesday = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(14)]
    public void GivenAnyWeek_WhenLaidOutFromNow_ThenTheAppCallsItCurrentAndInSeason(int week)
    {
        IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.Build(2026, week, Tuesday);

        SeasonCalendar.CurrentWeekAt(Tuesday, weeks).Should().Be(new CurrentWeek(week, SeasonState.InSeason));
        SeasonCalendar.CurrentWeekAt(Tuesday.AddDays(6.9), weeks).Week.Should().Be(week, "the week lasts seven days from when it started");
    }

    [Fact]
    public void GivenALayout_WhenInspected_ThenWindowsAreContiguousOrderedAndOnlyChampionshipWeekIsNotRegular()
    {
        IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.Build(2026, 5, Tuesday);

        weeks.Select(week => week.Week).Should().Equal(Enumerable.Range(1, 15));
        for (int index = 1; index < weeks.Count; index++)
        {
            (weeks[index].StartUtc - weeks[index - 1].EndUtc).Should().Be(SimCalendarLayout.Gap);
        }

        weeks.Where(week => !week.IsRegularSeason).Select(week => week.Week).Should().Equal(15);
        SeasonCalendar.DefaultLeagueRange(weeks).Should().Be(new LeagueWeekRange(1, 14));
    }

    [Fact]
    public void GivenTheNextWeekStartedLater_WhenLaidOut_ThenTheEarlierWeekIsOverAndTheNextIsCurrent()
    {
        DateTimeOffset later = Tuesday.AddHours(3);
        IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.Build(2026, 6, later);

        SeasonCalendar.CurrentWeekAt(later, weeks).Week.Should().Be(6);
        weeks.Single(week => week.Week == 5).EndUtc.Should().BeBefore(later);
    }

    [Fact]
    public void GivenBeforeSeason_WhenLaidOut_ThenTheAppSaysTheSeasonHasNotStarted()
    {
        SeasonCalendar.CurrentWeekAt(Tuesday, SimCalendarLayout.BeforeSeason(2026, Tuesday))
            .Should().Be(new CurrentWeek(1, SeasonState.BeforeSeason));
    }

    [Fact]
    public void GivenSeasonOver_WhenLaidOut_ThenTheAppSaysTheSeasonIsOverAndLeaguesAreComplete()
    {
        IReadOnlyList<SeasonWeek> weeks = SimCalendarLayout.SeasonOver(2026, Tuesday);

        SeasonCalendar.CurrentWeekAt(Tuesday, weeks).Should().Be(new CurrentWeek(14, SeasonState.SeasonOver));
        SeasonCalendar.LeagueIsCompleteAt(Tuesday, 14, weeks).Should().BeTrue();
    }
}
