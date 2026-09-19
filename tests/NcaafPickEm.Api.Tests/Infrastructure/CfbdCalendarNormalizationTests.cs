using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Cfbd;
using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="CfbdCalendarNormalization"/> (P2-02): CFBD's Monday-03:00-ET-through-next-Monday-
/// 02:59-ET calendar week normalizes to the Sunday-through-Saturday Eastern
/// <see cref="SeasonWeek"/> window <c>04-Domain-Algorithms.md</c> section 1 uses. Values below are
/// the real 2025 week 3 boundaries from <c>tests/NcaafPickEm.Fixtures/Real/cfbd-calendar-2025.json</c>.
/// </summary>
public sealed class CfbdCalendarNormalizationTests
{
    [Fact]
    public void GivenAMondayZeroThreeEasternWindow_WhenFindingTheSaturday_ThenItIsTheOneInsideTheWindow()
    {
        // 2025-09-08T07:00:00Z = Monday 03:00 EDT; 2025-09-15T06:59:00Z = the following Monday
        // 02:59 EDT.
        DateOnly saturday = CfbdCalendarNormalization.FindSaturdayEastern(
            new DateTime(2025, 9, 8, 7, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 9, 15, 6, 59, 0, DateTimeKind.Utc));

        saturday.Should().Be(new DateOnly(2025, 9, 13));
    }

    [Fact]
    public void GivenACfbdCalendarWeek_WhenNormalized_ThenTheWindowIsSundayThroughSaturdayEastern()
    {
        var providerWeek = new ProviderCalendarWeek(
            2025,
            3,
            "regular",
            new DateTime(2025, 9, 8, 7, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 9, 15, 6, 59, 0, DateTimeKind.Utc));

        SeasonWeek normalized = CfbdCalendarNormalization.Normalize(providerWeek);

        (DateTimeOffset expectedStart, DateTimeOffset expectedEnd) =
            SeasonCalendar.WeekWindow(new DateOnly(2025, 9, 13));

        normalized.SeasonYear.Should().Be(2025);
        normalized.Week.Should().Be(3);
        normalized.IsRegularSeason.Should().BeTrue();
        normalized.StartUtc.Should().Be(expectedStart);
        normalized.EndUtc.Should().Be(expectedEnd);

        // Sunday 2025-09-07 00:00 ET through Saturday 2025-09-13 23:59:59.999 ET, not CFBD's own
        // Monday-to-Monday boundary.
        SeasonCalendar.ToEastern(normalized.StartUtc).Should().Be(
            new DateTimeOffset(2025, 9, 7, 0, 0, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void GivenAWeekThatCrossesTheFallBackTransition_WhenNormalized_ThenBothEndsUseTheirOwnOffset()
    {
        // 2025 DST ends Sunday 2025-11-02 at 02:00 ET. CFBD's week 11 window runs Monday
        // 2025-11-03 03:00 EST (08:00Z) through the following Monday 02:59 EST, so the Saturday
        // inside it is 2025-11-08 and the normalized window opens on Sunday 2025-11-02 — the day
        // the clocks change.
        var providerWeek = new ProviderCalendarWeek(
            2025,
            11,
            "regular",
            new DateTime(2025, 11, 3, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 10, 7, 59, 0, DateTimeKind.Utc));

        SeasonWeek normalized = CfbdCalendarNormalization.Normalize(providerWeek);

        // Sunday 2025-11-02 00:00 is still EDT (-04:00); Saturday 2025-11-08 23:59:59.999 is EST
        // (-05:00). A naive fixed-offset conversion would be an hour out on one of the two ends.
        normalized.StartUtc.Should().Be(new DateTimeOffset(2025, 11, 2, 4, 0, 0, TimeSpan.Zero));
        normalized.EndUtc.Should().Be(new DateTimeOffset(2025, 11, 9, 4, 59, 59, 999, TimeSpan.Zero));
        (normalized.EndUtc - normalized.StartUtc).Should().Be(TimeSpan.FromDays(7).Add(TimeSpan.FromHours(1)) - TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void GivenAPostseasonWeek_WhenNormalized_ThenIsRegularSeasonIsFalse()
    {
        var providerWeek = new ProviderCalendarWeek(
            2025,
            17,
            "postseason",
            new DateTime(2025, 12, 1, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 8, 7, 59, 0, DateTimeKind.Utc));

        SeasonWeek normalized = CfbdCalendarNormalization.Normalize(providerWeek);

        normalized.IsRegularSeason.Should().BeFalse();
    }

    [Theory]
    [InlineData("regular", true)]
    [InlineData("Regular", true)]
    [InlineData("REGULAR", true)]
    [InlineData(" regular ", true)]
    [InlineData("postseason", false)]
    [InlineData("Postseason", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GivenASeasonTypeSpelling_WhenTested_ThenTheComparisonIgnoresCase(string? seasonType, bool expected)
    {
        // The Kiota client hands back the enum's own "Regular"; the raw JSON says "regular".
        CfbdCalendarNormalization.IsRegularSeasonType(seasonType).Should().Be(expected);
    }

    [Fact]
    public void GivenTheShapeOfTheReal2026Calendar_WhenTheSeasonIsNormalized_ThenOnlyRegularWeeksSurvive()
    {
        // 15 regular weeks plus the single postseason row CFBD numbers 1 - the collision with
        // regular week 1 that made the first live ingest throw (D-167).
        List<ProviderCalendarWeek> providerWeeks = [.. Enumerable.Range(1, 15).Select(CfbdWeek)];
        providerWeeks.Add(new ProviderCalendarWeek(
            2026,
            1,
            "Postseason",
            new DateTime(2026, 12, 14, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 25, 7, 59, 0, DateTimeKind.Utc)));

        CalendarNormalizationResult result = CfbdCalendarNormalization.NormalizeSeason(providerWeeks);

        result.SkippedNonRegular.Should().Be(1);
        result.DuplicateWeeks.Should().BeEmpty();
        result.Weeks.Select(week => week.Week).Should().Equal(Enumerable.Range(1, 15));

        // Championship week is the last regular week CFBD reports, and 04-Domain-Algorithms.md
        // section 1 puts it out of scope, so a league defaults to 1..14.
        result.Weeks.Single(week => week.Week == 15).IsRegularSeason.Should().BeFalse();
        result.Weeks.Where(week => week.Week < 15).Should().OnlyContain(week => week.IsRegularSeason);
        SeasonCalendar.DefaultLeagueRange([.. result.Weeks]).Should().Be(new LeagueWeekRange(1, 14));
    }

    [Fact]
    public void GivenARepeatedWeekNumber_WhenTheSeasonIsNormalized_ThenTheFirstRowWins()
    {
        List<ProviderCalendarWeek> providerWeeks = [CfbdWeek(1), CfbdWeek(2), CfbdWeek(1), CfbdWeek(3)];

        CalendarNormalizationResult result = CfbdCalendarNormalization.NormalizeSeason(providerWeeks);

        result.DuplicateWeeks.Should().Equal(1);
        result.Weeks.Select(week => week.Week).Should().Equal(1, 2, 3);
        result.Weeks.Single(week => week.Week == 1).StartUtc
            .Should().Be(SeasonCalendar.WeekWindow(SaturdayOf(1)).StartUtc);
    }

    [Fact]
    public void GivenNothingButPostseasonRows_WhenTheSeasonIsNormalized_ThenNoWeeksAreStored()
    {
        CalendarNormalizationResult result = CfbdCalendarNormalization.NormalizeSeason(
        [
            new ProviderCalendarWeek(
                2026,
                1,
                "postseason",
                new DateTime(2026, 12, 14, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 25, 7, 59, 0, DateTimeKind.Utc)),
        ]);

        result.Weeks.Should().BeEmpty();
        result.SkippedNonRegular.Should().Be(1);
    }

    /// <summary>Week 1's Saturday, 2026-09-05, plus seven days per week after it.</summary>
    private static DateOnly SaturdayOf(int week) => new DateOnly(2026, 9, 5).AddDays(7 * (week - 1));

    /// <summary>
    /// A CFBD-shaped row for <paramref name="week"/>: Monday-before through Monday-after in UTC,
    /// with exactly one Saturday inside it whatever side of the DST change it falls on.
    /// </summary>
    private static ProviderCalendarWeek CfbdWeek(int week)
    {
        DateOnly saturday = SaturdayOf(week);
        return new ProviderCalendarWeek(
            2026,
            week,
            "Regular",
            saturday.AddDays(-5).ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc),
            saturday.AddDays(2).ToDateTime(new TimeOnly(6, 59), DateTimeKind.Utc));
    }
}
