using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Pure decision-table checks for <see cref="SaturdayPollerSchedule"/>
/// (04-Domain-Algorithms.md section 10). No database, no clock injection: every case constructs
/// its own <see cref="PollerWeekSet"/> rows and calls <see cref="SaturdayPollerSchedule.Evaluate"/>
/// directly.
/// </summary>
public sealed class SaturdayPollerScheduleTests
{
    // A Saturday lock time: 2026-10-17 (fixture Saturday) 12:00 ET = 16:00 UTC (EDT, UTC-4).
    private static readonly DateTime LockAtUtc = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GivenNoActiveSets_WhenEvaluated_ThenThereIsNoWindow()
    {
        SaturdayPollerSchedule.Evaluate(LockAtUtc, [], LiveScoreSource.Espn)
            .Should().Be(new PollerDecision(false, null, PollerCadence.FiveMinutes));
    }

    [Fact]
    public void GivenOnlySetsWithNoActiveGame_WhenEvaluated_ThenThereIsNoWindow()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), null, true)];

        SaturdayPollerSchedule.Evaluate(LockAtUtc, sets, LiveScoreSource.Espn).InWindow
            .Should().BeFalse();
    }

    [Fact]
    public void GivenNowBeforeWindowOpens_WhenEvaluated_ThenItReportsTheWindowStartButNotInWindow()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false)];
        DateTimeOffset justBeforeOpen = new DateTimeOffset(LockAtUtc).AddMinutes(-6);

        PollerDecision decision = SaturdayPollerSchedule.Evaluate(justBeforeOpen, sets, LiveScoreSource.Espn);

        decision.InWindow.Should().BeFalse();
        decision.NextPollAtUtc.Should().Be(new DateTimeOffset(LockAtUtc).AddMinutes(-5));
    }

    [Fact]
    public void GivenNowAtWindowStart_WhenEvaluated_ThenItIsInWindow()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false)];
        DateTimeOffset windowStart = new DateTimeOffset(LockAtUtc).AddMinutes(-5);

        SaturdayPollerSchedule.Evaluate(windowStart, sets, LiveScoreSource.Espn).InWindow
            .Should().BeTrue();
    }

    [Fact]
    public void GivenWindowOpen_WhenTheEarliestOfSeveralLeaguesLockTimesIsUsed_ThenItOpensAtTheEarliestMinusFiveMinutes()
    {
        DateTime laterLock = LockAtUtc.AddHours(3);
        PollerWeekSet[] sets =
        [
            new PollerWeekSet(Guid.NewGuid(), laterLock, false),
            new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false),
        ];

        DateTimeOffset expectedStart = new DateTimeOffset(LockAtUtc).AddMinutes(-5);

        SaturdayPollerSchedule.Evaluate(expectedStart.AddMinutes(-1), sets, LiveScoreSource.Espn).NextPollAtUtc
            .Should().Be(expectedStart);
        SaturdayPollerSchedule.Evaluate(expectedStart, sets, LiveScoreSource.Espn).InWindow.Should().BeTrue();
    }

    [Fact]
    public void GivenEveryActiveGameTerminal_WhenEvaluated_ThenTheWindowIsClosedEvenInsideItsHours()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, true)];
        DateTimeOffset midAfternoon = new DateTimeOffset(LockAtUtc).AddHours(2);

        PollerDecision decision = SaturdayPollerSchedule.Evaluate(midAfternoon, sets, LiveScoreSource.Espn);

        decision.InWindow.Should().BeFalse();
        decision.NextPollAtUtc.Should().BeNull();
    }

    [Fact]
    public void GivenANonFinalGameStillPlaying_WhenNowReachesHardCloseSundayThreeAmEastern_ThenTheWindowIsClosedAnyway()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false)];

        // Saturday 2026-10-17 -> hard close is Sunday 2026-10-18 03:00 ET = 07:00 UTC.
        DateTimeOffset hardClose = new(2026, 10, 18, 7, 0, 0, TimeSpan.Zero);
        DateTimeOffset justBeforeClose = hardClose.AddMinutes(-1);

        SaturdayPollerSchedule.Evaluate(justBeforeClose, sets, LiveScoreSource.Espn).InWindow.Should().BeTrue();

        PollerDecision atClose = SaturdayPollerSchedule.Evaluate(hardClose, sets, LiveScoreSource.Espn);
        atClose.InWindow.Should().BeFalse();
        atClose.NextPollAtUtc.Should().BeNull();
    }

    [Fact]
    public void GivenAWindowOpenPastMidnight_WhenEvaluated_ThenItStillReportsTheSaturdayEasternDate()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false)];

        // Sunday 2026-10-18 01:30 ET (EDT, UTC-4) = 05:30 UTC.
        DateTimeOffset afterMidnight = new(2026, 10, 18, 5, 30, 0, TimeSpan.Zero);

        PollerDecision decision = SaturdayPollerSchedule.Evaluate(afterMidnight, sets, LiveScoreSource.Espn);

        decision.InWindow.Should().BeTrue();
        decision.SaturdayEastern.Should().Be(
            new DateOnly(2026, 10, 17),
            "ESPN buckets the post-midnight final on the Saturday date it kicked off");
    }

    [Fact]
    public void GivenTheNovemberDstSaturday_WhenEvaluated_ThenTheHardCloseIsThreeAmEasternStandardTime()
    {
        // Saturday 2026-10-31 19:00 ET (EDT, UTC-4) = 23:00 UTC. Eastern falls back at 02:00 on
        // Sunday 2026-11-01, so 03:00 ET that morning is EST (UTC-5) = 08:00 UTC, not 07:00.
        var lockAtUtc = new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), lockAtUtc, false)];

        DateTimeOffset hardClose = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

        SaturdayPollerSchedule.Evaluate(hardClose.AddMinutes(-1), sets, LiveScoreSource.Espn).InWindow
            .Should().BeTrue("07:59 UTC is 02:59 EST, one minute short of the hard close");
        SaturdayPollerSchedule.Evaluate(hardClose, sets, LiveScoreSource.Espn).InWindow
            .Should().BeFalse("08:00 UTC is exactly 03:00 EST");
    }

    [Theory]
    [InlineData(LiveScoreSource.Espn, PollerCadence.FiveMinutes)]
    [InlineData(LiveScoreSource.Cfbd, PollerCadence.TenMinutes)]
    [InlineData(LiveScoreSource.Fixture, PollerCadence.FiveMinutes)]
    public void GivenAnActiveSource_WhenEvaluated_ThenTheCadenceMatchesThatSource(
        LiveScoreSource source, PollerCadence expectedCadence)
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false)];
        DateTimeOffset inWindow = new DateTimeOffset(LockAtUtc).AddMinutes(-5);

        SaturdayPollerSchedule.Evaluate(inWindow, sets, source).Cadence.Should().Be(expectedCadence);
    }

    [Fact]
    public void GivenTheEspnFallbackHasEngaged_WhenEvaluatedAgain_ThenTheCadenceSwitchesToTenMinutes()
    {
        PollerWeekSet[] sets = [new PollerWeekSet(Guid.NewGuid(), LockAtUtc, false)];
        DateTimeOffset inWindow = new DateTimeOffset(LockAtUtc).AddMinutes(-5);

        PollerDecision beforeFallback = SaturdayPollerSchedule.Evaluate(inWindow, sets, LiveScoreSource.Espn);
        PollerDecision afterFallback = SaturdayPollerSchedule.Evaluate(inWindow, sets, LiveScoreSource.Cfbd);

        beforeFallback.Cadence.Should().Be(PollerCadence.FiveMinutes);
        afterFallback.Cadence.Should().Be(PollerCadence.TenMinutes);
    }
}
