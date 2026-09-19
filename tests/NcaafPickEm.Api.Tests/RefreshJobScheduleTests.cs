using Cronos;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Jobs.Refresh;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Pure cron checks for the P2-04 refresh jobs: each job's <c>CronExpression</c> resolves to the
/// Eastern times the card names, including across the 2026-11-01 DST change (04-Domain-Algorithms.md
/// section 10; no database, no clock injection).
/// </summary>
public sealed class RefreshJobScheduleTests
{
    [Fact]
    public void GivenTeamsRefreshJob_WhenParsed_ThenItFiresTuesday0300Eastern()
    {
        // 2026-09-22 is a Tuesday, inside EDT (UTC-4).
        AssertSingleOccurrence(
            new TeamsRefreshJob(null!, null!, null!).CronExpression,
            new DateTime(2026, 9, 21),
            new DateTime(2026, 9, 23),
            new DateTimeOffset(2026, 9, 22, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GivenScheduleRefreshJob_WhenParsed_ThenItFiresTuesday0310Eastern()
    {
        AssertSingleOccurrence(
            new ScheduleRefreshJob(null!).CronExpression,
            new DateTime(2026, 9, 21),
            new DateTime(2026, 9, 23),
            new DateTimeOffset(2026, 9, 22, 7, 10, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GivenScheduleRefreshDailyJob_WhenParsed_ThenItFiresWednesdayThroughSaturdayAt0400Eastern()
    {
        CronExpression cron = CronExpression.Parse(new ScheduleRefreshDailyJob(null!).CronExpression);

        DateTimeOffset[] occurrences =
        [
            .. cron.GetOccurrences(
                new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero),
                SeasonCalendar.Eastern),
        ];

        // Wed 9/23, Thu 9/24, Fri 9/25, Sat 9/26 at 04:00 EDT (UTC-4) = 08:00 UTC.
        occurrences.Should().BeEquivalentTo(
        [
            new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
        ]);
    }

    [Fact]
    public void GivenRankingsRefreshJobs_WhenParsed_ThenTheyFireThreeTimesPerWeek()
    {
        CronExpression evening = CronExpression.Parse(new RankingsRefreshEveningJob(null!).CronExpression);
        CronExpression tuesday = CronExpression.Parse(new RankingsRefreshTuesdayJob(null!).CronExpression);

        DateTimeOffset from = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero); // Sunday
        DateTimeOffset to = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero); // next Sunday

        DateTimeOffset[] eveningOccurrences = [.. evening.GetOccurrences(from, to, SeasonCalendar.Eastern)];
        DateTimeOffset[] tuesdayOccurrences = [.. tuesday.GetOccurrences(from, to, SeasonCalendar.Eastern)];

        eveningOccurrences.Should().BeEquivalentTo(
        [
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), // Sun 9/20 20:00 EDT
            new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero), // Mon 9/21 20:00 EDT
        ]);
        tuesdayOccurrences.Should().BeEquivalentTo(
        [
            new DateTimeOffset(2026, 9, 22, 7, 20, 0, TimeSpan.Zero), // Tue 9/22 03:20 EDT
        ]);

        (eveningOccurrences.Length + tuesdayOccurrences.Length).Should().Be(3);
    }

    [Fact]
    public void GivenLinesRefreshJob_WhenParsedAcrossTheDstChange_ThenBothSidesResolveCorrectly()
    {
        CronExpression cron = CronExpression.Parse(new LinesRefreshJob(null!, null!, null!, null!).CronExpression);

        // Fall-back happens 2026-11-01 02:00 ET (Eastern DST ends the first Sunday of November).
        // Halloween night (still EDT, UTC-4) and the next night (already EST, UTC-5) both fire.
        DateTimeOffset[] occurrences =
        [
            .. cron.GetOccurrences(
                new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero),
                SeasonCalendar.Eastern),
        ];

        occurrences.Should().BeEquivalentTo(
        [
            new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero), // Oct 31 23:30 EDT
            new DateTimeOffset(2026, 11, 2, 4, 30, 0, TimeSpan.Zero), // Nov 1 23:30 EST
        ]);
    }

    private static void AssertSingleOccurrence(
        string cronExpression,
        DateTime fromEastern,
        DateTime toEastern,
        DateTimeOffset expectedUtc)
    {
        CronExpression cron = CronExpression.Parse(cronExpression);

        DateTimeOffset[] occurrences =
        [
            .. cron.GetOccurrences(
                SeasonCalendar.ToUtc(fromEastern),
                SeasonCalendar.ToUtc(toEastern),
                SeasonCalendar.Eastern),
        ];

        occurrences.Should().ContainSingle().Which.Should().Be(expectedUtc);
    }
}
