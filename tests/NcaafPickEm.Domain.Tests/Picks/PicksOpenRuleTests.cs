using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Domain.Tests.Picks;

/// <summary>
/// Feature 16 (D-190, 04-Domain-Algorithms.md section 4.1): a week's picks open Tuesday 04:00 ET
/// of its window, and only once its set has games.
/// </summary>
public sealed class PicksOpenRuleTests
{
    // Week 7 of 2026 runs Sun 11 Oct to Sat 17 Oct ET (EDT, UTC-4): picks open Tue 13 Oct 08:00 UTC.
    private static readonly SeasonWeek Week7 = WeekEnding(new DateOnly(2026, 10, 17), 7);

    // Week 10 starts Sun 1 Nov, the fall-back Sunday (EDT until 02:00, then EST, UTC-5): picks open
    // Tue 3 Nov 04:00 EST = 09:00 UTC.
    private static readonly SeasonWeek Week10 = WeekEnding(new DateOnly(2026, 11, 7), 10);

    // Week 9 straddles nothing but ends the night before the fall-back: Tue 27 Oct 04:00 EDT = 08:00 UTC.
    private static readonly SeasonWeek Week9 = WeekEnding(new DateOnly(2026, 10, 31), 9);

    private static SeasonWeek WeekEnding(DateOnly saturday, int week)
    {
        (DateTimeOffset startUtc, DateTimeOffset endUtc) = SeasonCalendar.WeekWindow(saturday);
        return new SeasonWeek(2026, week, startUtc, endUtc, IsRegularSeason: true);
    }

    [Fact]
    public void GivenAnEdtWeek_ThenPicksOpenTuesdayAtFourEastern()
    {
        PicksOpenRule.OpensAtUtc(Week7).Should().Be(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
        SeasonCalendar.EasternDisplay(PicksOpenRule.OpensAtUtc(Week7)).Should().Be("Tue 4:00 AM ET");
    }

    [Fact]
    public void GivenTheFallBackWeek_ThenPicksStillOpenAtFourEasternStandardTime()
    {
        PicksOpenRule.OpensAtUtc(Week10).Should().Be(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
        PicksOpenRule.OpensAtUtc(Week9).Should().Be(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2026-10-11T04:00:00Z")] // Sunday 00:00 ET, the week's first instant
    [InlineData("2026-10-13T03:59:59Z")] // Monday 23:59:59 ET
    [InlineData("2026-10-13T04:00:00Z")] // Tuesday 00:00 ET: still before the 04:00 open (Open Question b)
    [InlineData("2026-10-13T07:59:59Z")] // Tuesday 03:59:59 ET
    public void GivenBeforeTheOpenMoment_ThenPicksAreNotYetOpen(string nowUtc)
    {
        PicksOpening opening = PicksOpenRule.At(DateTimeOffset.Parse(nowUtc, System.Globalization.CultureInfo.InvariantCulture), Week7, hasActiveGames: true);

        opening.State.Should().Be(PicksOpenState.NotYetOpen);
        opening.IsOpen.Should().BeFalse();
        opening.OpensAtUtc.Should().Be(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2026-10-13T08:00:00Z")] // Tuesday 04:00:00 ET exactly
    [InlineData("2026-10-17T15:59:59Z")] // Saturday, just before a noon lock
    public void GivenAtOrAfterTheOpenMoment_ThenPicksAreOpen(string nowUtc)
    {
        PicksOpenRule.At(DateTimeOffset.Parse(nowUtc, System.Globalization.CultureInfo.InvariantCulture), Week7, hasActiveGames: true)
            .IsOpen.Should().BeTrue();
    }

    [Fact]
    public void GivenTheFallBackWeek_WhenTheOldOffsetWouldHaveOpenedIt_ThenItIsStillClosed()
    {
        // 08:00 UTC Tuesday 3 Nov is 03:00 EST: an open computed as "start + 52 hours" would be wrong here.
        PicksOpenRule.At(new DateTimeOffset(2026, 11, 3, 8, 59, 59, TimeSpan.Zero), Week10, hasActiveGames: true)
            .State.Should().Be(PicksOpenState.NotYetOpen);
        PicksOpenRule.At(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), Week10, hasActiveGames: true)
            .State.Should().Be(PicksOpenState.Open);
    }

    [Fact]
    public void GivenTheMomentHasPassedButTheSetHasNoGames_ThenPicksAwaitTheGames()
    {
        PicksOpening opening = PicksOpenRule.At(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero), Week7, hasActiveGames: false);

        opening.State.Should().Be(PicksOpenState.AwaitingGames);
        opening.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void GivenNoSeasonCalendar_ThenOnlyTheSetDecides()
    {
        DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        PicksOpenRule.At(now, week: null, hasActiveGames: true).Should().Be(new PicksOpening(null, PicksOpenState.Open));
        PicksOpenRule.At(now, week: null, hasActiveGames: false).State.Should().Be(PicksOpenState.AwaitingGames);
    }

    [Fact]
    public void GivenTheOpenMoment_ThenHasOpenMomentPassedFlipsExactlyThere()
    {
        DateTimeOffset opensAt = PicksOpenRule.OpensAtUtc(Week7);

        PicksOpenRule.HasOpenMomentPassed(opensAt.AddTicks(-1), Week7).Should().BeFalse();
        PicksOpenRule.HasOpenMomentPassed(opensAt, Week7).Should().BeTrue();
    }
}
