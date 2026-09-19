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
}
