using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Domain.Tests.Seasons;

/// <summary>
/// Feature 13 and 04-Domain-Algorithms.md section 1: week windows, Saturday determination,
/// the current week, and the default league range. One test per acceptance criterion.
/// </summary>
public sealed class SeasonCalendarTests
{
    // The 2026 season as the fixture source builds it: Week 1 is the week of Saturday
    // 5 September 2026, Week 0 the week before, Week 15 is championship week.
    private static readonly DateOnly WeekOneSaturday = new(2026, 9, 5);
    private const int ChampionshipWeek = 15;

    private static readonly IReadOnlyList<SeasonWeek> Weeks = BuildWeeks();

    private static SeasonWeek Week(int week) => Weeks.Single(w => w.Week == week);

    private static IReadOnlyList<SeasonWeek> BuildWeeks()
    {
        List<SeasonWeek> weeks = [];
        for (int week = 0; week <= ChampionshipWeek; week++)
        {
            DateOnly saturday = WeekOneSaturday.AddDays(7 * (week - 1));
            (DateTimeOffset startUtc, DateTimeOffset endUtc) = SeasonCalendar.WeekWindow(saturday);
            weeks.Add(new SeasonWeek(2026, week, startUtc, endUtc, IsRegularSeason: week < ChampionshipWeek));
        }

        return weeks;
    }

    // ------------------------------------------------------------------ Saturday determination

    [Fact]
    public void GivenFridayLatePacificKickoff_WhenAskingIfSaturday_ThenItCountsAsSaturdayEastern()
    {
        // Friday 6 November 2026, 11:30 PM Pacific (PST, UTC-8) = Saturday 2:30 AM Eastern.
        DateTimeOffset kickoffUtc = new(2026, 11, 7, 7, 30, 0, TimeSpan.Zero);

        SeasonCalendar.IsSaturdayEastern(kickoffUtc).Should().BeTrue();
        SeasonCalendar.ToEastern(kickoffUtc).Hour.Should().Be(2);
    }

    [Fact]
    public void GivenFridayNightEasternKickoff_WhenAskingIfSaturday_ThenItIsNotSaturday()
    {
        // Friday 6 November 2026, 8:00 PM Eastern (EST, UTC-5).
        DateTimeOffset kickoffUtc = new(2026, 11, 7, 1, 0, 0, TimeSpan.Zero);

        SeasonCalendar.IsSaturdayEastern(kickoffUtc).Should().BeFalse();
    }

    [Fact]
    public void GivenSaturdayLateEasternKickoff_WhenAskingIfSaturday_ThenItIsSaturday()
    {
        // Saturday 7 November 2026, 10:30 PM Eastern - a game that will finish after midnight.
        DateTimeOffset kickoffUtc = new(2026, 11, 8, 3, 30, 0, TimeSpan.Zero);

        SeasonCalendar.IsSaturdayEastern(kickoffUtc).Should().BeTrue();
    }

    // ------------------------------------------------------------------------- week boundaries

    [Fact]
    public void GivenAWeekInsideDaylightTime_WhenBuildingItsWindow_ThenItRunsSundayMidnightToSaturdayMidnightEdt()
    {
        // Week 9: Sunday 25 October through Saturday 31 October 2026, all in EDT (UTC-4).
        SeasonWeek week9 = Week(9);

        week9.StartUtc.Should().Be(new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.Zero));
        week9.EndUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 3, 59, 59, 999, TimeSpan.Zero));
        (week9.EndUtc - week9.StartUtc).Should().Be(TimeSpan.FromDays(7) - TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void GivenTheWeekOfTheNovemberDstChange_WhenBuildingItsWindow_ThenItIsAnHourLongerInUtc()
    {
        // Week 10 opens Sunday 1 November 2026 at 00:00 EDT (UTC-4) - two hours before the clocks
        // go back - and closes Saturday 7 November at 23:59:59.999 EST (UTC-5).
        SeasonWeek week10 = Week(10);

        week10.StartUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
        week10.EndUtc.Should().Be(new DateTimeOffset(2026, 11, 8, 4, 59, 59, 999, TimeSpan.Zero));
        (week10.EndUtc - week10.StartUtc).Should()
            .Be(TimeSpan.FromDays(7) + TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void GivenConsecutiveWeeksAcrossTheDstChange_WhenComparingWindows_ThenTheyAbutExactly()
    {
        Week(10).StartUtc.Should().Be(Week(9).EndUtc.AddMilliseconds(1));
        Week(11).StartUtc.Should().Be(Week(10).EndUtc.AddMilliseconds(1));
    }

    [Fact]
    public void GivenAWeekWindow_WhenRenderedInEastern_ThenItStartsAndEndsAtLocalMidnight()
    {
        SeasonWeek week10 = Week(10);

        DateTimeOffset startEastern = SeasonCalendar.ToEastern(week10.StartUtc);
        DateTimeOffset endEastern = SeasonCalendar.ToEastern(week10.EndUtc);

        startEastern.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        startEastern.TimeOfDay.Should().Be(TimeSpan.Zero);
        endEastern.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        endEastern.TimeOfDay.Should().Be(new TimeSpan(0, 23, 59, 59, 999));
    }

    [Fact]
    public void GivenANonSaturdayDate_WhenBuildingAWeekWindow_ThenItIsRejected()
    {
        Action build = () => SeasonCalendar.WeekWindow(new DateOnly(2026, 9, 4));

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GivenAnEasternWallClockTime_WhenConvertedToUtcAndBack_ThenItRoundTrips()
    {
        DateTime easternLocal = new(2026, 11, 7, 12, 0, 0);

        DateTimeOffset utc = SeasonCalendar.ToUtc(easternLocal);

        utc.Should().Be(new DateTimeOffset(2026, 11, 7, 17, 0, 0, TimeSpan.Zero));
        SeasonCalendar.ToEastern(utc).DateTime.Should().Be(easternLocal);
    }

    // ------------------------------------------------------------------------- current week

    [Fact]
    public void GivenNowBeforeTheFirstWeek_WhenResolvingTheCurrentWeek_ThenItIsTheFirstWeekAndTheSeasonHasNotStarted()
    {
        DateTimeOffset beforeKickoff = Weeks[0].StartUtc.AddDays(-1);

        CurrentWeek current = SeasonCalendar.CurrentWeekAt(beforeKickoff, Weeks);

        current.Week.Should().Be(0);
        current.State.Should().Be(SeasonState.BeforeSeason);
        current.IsSeasonOver.Should().BeFalse();
    }

    [Fact]
    public void GivenNowInsideAWeekWindow_WhenResolvingTheCurrentWeek_ThenItIsThatWeek()
    {
        // Wednesday of Week 7.
        DateTimeOffset midweek = Week(7).StartUtc.AddDays(3);

        CurrentWeek current = SeasonCalendar.CurrentWeekAt(midweek, Weeks);

        current.Week.Should().Be(7);
        current.State.Should().Be(SeasonState.InSeason);
    }

    [Fact]
    public void GivenNowAtSundayMidnightEastern_WhenResolvingTheCurrentWeek_ThenTheOldWeekIsNoLongerCurrent()
    {
        // The instant Week 7 closes belongs to Week 7; one millisecond later is Week 8, even
        // though Week 7 games may still be being scored (Feature 13).
        SeasonCalendar.CurrentWeekAt(Week(7).EndUtc, Weeks).Week.Should().Be(7);
        SeasonCalendar.CurrentWeekAt(Week(7).EndUtc.AddMilliseconds(1), Weeks).Week.Should().Be(8);
    }

    [Fact]
    public void GivenNowInsideChampionshipWeek_WhenResolvingTheCurrentWeek_ThenTheSeasonIsOverAtTheLastRegularWeek()
    {
        DateTimeOffset championshipWednesday = Week(ChampionshipWeek).StartUtc.AddDays(3);

        CurrentWeek current = SeasonCalendar.CurrentWeekAt(championshipWednesday, Weeks);

        current.Week.Should().Be(14);
        current.State.Should().Be(SeasonState.SeasonOver);
        current.IsSeasonOver.Should().BeTrue();
    }

    [Fact]
    public void GivenNowAfterTheWholeSeason_WhenResolvingTheCurrentWeek_ThenTheSeasonIsOver()
    {
        DateTimeOffset newYear = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        CurrentWeek current = SeasonCalendar.CurrentWeekAt(newYear, Weeks);

        current.Week.Should().Be(14);
        current.State.Should().Be(SeasonState.SeasonOver);
    }

    [Fact]
    public void GivenAClock_WhenResolvingTheCurrentWeekFromIt_ThenItMatchesTheExplicitInstant()
    {
        DateTimeOffset midweek = Week(3).StartUtc.AddDays(2);
        SeasonCalendar calendar = new(new FixedTimeProvider(midweek));

        calendar.CurrentWeekNow(Weeks).Should().Be(SeasonCalendar.CurrentWeekAt(midweek, Weeks));
        calendar.UtcNow.Should().Be(midweek);
    }

    [Fact]
    public void GivenNoWeeks_WhenResolvingTheCurrentWeek_ThenItIsRejected()
    {
        Action resolve = () => SeasonCalendar.CurrentWeekAt(DateTimeOffset.UtcNow, []);

        resolve.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------- default league range

    [Fact]
    public void GivenASeasonWithWeekZeroAndChampionshipWeek_WhenTakingTheDefaultRange_ThenItIsWeekOneToTheLastRegularWeek()
    {
        LeagueWeekRange range = SeasonCalendar.DefaultLeagueRange(Weeks);

        range.FirstWeek.Should().Be(1, "Week 0 is never included by default");
        range.LastWeek.Should().Be(14, "championship week is out of scope");
        range.Contains(0).Should().BeFalse();
        range.Contains(ChampionshipWeek).Should().BeFalse();
        range.Contains(1).Should().BeTrue();
        range.Contains(14).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0)]      // a commissioner may choose Week 0 explicitly
    [InlineData(-5, 0)]
    [InlineData(7, 7)]
    [InlineData(ChampionshipWeek, 14)]
    [InlineData(99, 14)]
    public void GivenAWeekOutsideTheRegularSeason_WhenClamped_ThenItLandsOnARegularSeasonWeek(int requested, int expected)
    {
        SeasonCalendar.ClampToRegularSeason(requested, Weeks).Should().Be(expected);
    }

    // ------------------------------------------------------------------------ league complete

    [Fact]
    public void GivenTheLastWeekHasNotClosed_WhenAskingIfTheLeagueIsComplete_ThenItIsNot()
    {
        SeasonCalendar.LeagueIsCompleteAt(Week(14).EndUtc, 14, Weeks).Should().BeFalse();
    }

    [Fact]
    public void GivenTheLastWeekHasClosed_WhenAskingIfTheLeagueIsComplete_ThenItIs()
    {
        SeasonCalendar.LeagueIsCompleteAt(Week(14).EndUtc.AddMilliseconds(1), 14, Weeks).Should().BeTrue();
    }

    [Fact]
    public void GivenALeagueEndingEarly_WhenItsLastWeekCloses_ThenItIsCompleteWhileTheSeasonRunsOn()
    {
        DateTimeOffset duringWeek9 = Week(9).StartUtc.AddDays(2);
        SeasonCalendar calendar = new(new FixedTimeProvider(duringWeek9));

        calendar.LeagueIsComplete(8, Weeks).Should().BeTrue();
        calendar.LeagueIsComplete(14, Weeks).Should().BeFalse();
        calendar.CurrentWeekNow(Weeks).State.Should().Be(SeasonState.InSeason);
    }

    [Fact]
    public void GivenAWeekOutsideTheSeason_WhenAskingIfTheLeagueIsComplete_ThenItIsRejected()
    {
        Action ask = () => SeasonCalendar.LeagueIsCompleteAt(DateTimeOffset.UtcNow, 42, Weeks);

        ask.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------- lock time hint

    [Fact]
    public void GivenANoonEasternKickoff_WhenRenderingTheEasternHint_ThenItReadsSaturdayNoonEt()
    {
        // Saturday 5 September 2026, 12:00 PM EDT.
        DateTimeOffset kickoffUtc = new(2026, 9, 5, 16, 0, 0, TimeSpan.Zero);

        SeasonCalendar.EasternDisplay(kickoffUtc).Should().Be("Sat 12:00 PM ET");
    }

    [Fact]
    public void GivenAKickoffInStandardTime_WhenRenderingTheEasternHint_ThenTheOffsetChangeIsHandled()
    {
        // Saturday 7 November 2026, 3:30 PM EST.
        DateTimeOffset kickoffUtc = new(2026, 11, 7, 20, 30, 0, TimeSpan.Zero);

        SeasonCalendar.EasternDisplay(kickoffUtc).Should().Be("Sat 3:30 PM ET");
    }
}
