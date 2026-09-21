using NcaafPickEm.Domain.Tests.Seasons;
using NcaafPickEm.Infrastructure.Time;

namespace NcaafPickEm.Domain.Tests.Time;

/// <summary>
/// <see cref="DevTimeProvider"/> (P10-01): real time plus an offset, or one frozen instant, over
/// a stubbed inner clock so "real time passing" is a single call.
/// </summary>
public sealed class DevTimeProviderTests
{
    private static readonly DateTimeOffset RealStart = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixtureWednesday = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenAFreshClock_WhenRead_ThenItIsRealTimeAndNotShifted()
    {
        (DevTimeProvider clock, _) = Create();

        clock.GetUtcNow().Should().Be(RealStart);
        clock.RealUtcNow.Should().Be(RealStart);
        clock.Offset.Should().Be(TimeSpan.Zero);
        clock.IsShifted.Should().BeFalse();
        clock.IsFrozen.Should().BeFalse();
    }

    [Fact]
    public void GivenSetNow_WhenRealTimePasses_ThenTheClockKeepsRunningFromThere()
    {
        (DevTimeProvider clock, FixedTimeProvider real) = Create();

        clock.SetNow(FixtureWednesday);
        real.Set(RealStart.AddSeconds(30));

        clock.GetUtcNow().Should().Be(FixtureWednesday.AddSeconds(30));
        clock.Offset.Should().Be(FixtureWednesday - RealStart);
        clock.IsShifted.Should().BeTrue();
        clock.IsFrozen.Should().BeFalse();
    }

    [Fact]
    public void GivenAdvance_WhenNegative_ThenTheClockMovesBackwards()
    {
        (DevTimeProvider clock, _) = Create();

        clock.Advance(TimeSpan.FromDays(1));
        clock.Advance(TimeSpan.FromHours(-2));

        clock.GetUtcNow().Should().Be(RealStart.AddHours(22));
    }

    [Fact]
    public void GivenFrozen_WhenRealTimePasses_ThenTheReadingDoesNotMove()
    {
        (DevTimeProvider clock, FixedTimeProvider real) = Create();
        clock.SetNow(FixtureWednesday);

        clock.Freeze();
        real.Set(RealStart.AddMinutes(10));

        clock.GetUtcNow().Should().Be(FixtureWednesday);
        clock.IsFrozen.Should().BeTrue();
        clock.IsShifted.Should().BeTrue();
        clock.Offset.Should().Be(FixtureWednesday - RealStart.AddMinutes(10), "a frozen clock falls further behind real time");
    }

    [Fact]
    public void GivenFrozen_WhenSetNowOrAdvanced_ThenItStaysFrozenAtTheNewInstant()
    {
        (DevTimeProvider clock, FixedTimeProvider real) = Create();
        clock.Freeze();

        clock.SetNow(FixtureWednesday);
        clock.Advance(TimeSpan.FromHours(3));
        real.Set(RealStart.AddHours(1));

        clock.GetUtcNow().Should().Be(FixtureWednesday.AddHours(3));
        clock.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void GivenFrozen_WhenThawed_ThenItRunsAgainFromTheFrozenReading()
    {
        (DevTimeProvider clock, FixedTimeProvider real) = Create();
        clock.SetNow(FixtureWednesday);
        clock.Freeze();
        real.Set(RealStart.AddMinutes(10));

        clock.Thaw();
        real.Set(RealStart.AddMinutes(15));

        clock.IsFrozen.Should().BeFalse();
        clock.GetUtcNow().Should().Be(FixtureWednesday.AddMinutes(5));
    }

    [Fact]
    public void GivenAnUnfrozenClock_WhenFrozenTwice_ThenTheSecondFreezeIsANoOp()
    {
        (DevTimeProvider clock, FixedTimeProvider real) = Create();

        clock.Freeze();
        real.Set(RealStart.AddMinutes(5));
        clock.Freeze();

        clock.GetUtcNow().Should().Be(RealStart);
    }

    [Fact]
    public void GivenAShiftedFrozenClock_WhenReset_ThenItReadsRealTimeAgain()
    {
        (DevTimeProvider clock, FixedTimeProvider real) = Create();
        clock.SetNow(FixtureWednesday);
        clock.Freeze();
        real.Set(RealStart.AddMinutes(1));

        clock.Reset();

        clock.GetUtcNow().Should().Be(RealStart.AddMinutes(1));
        clock.IsShifted.Should().BeFalse();
        clock.IsFrozen.Should().BeFalse();
        clock.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GivenAnyState_WhenTimestampsAreRead_ThenTheyStillComeFromTheRealClock()
    {
        // Only GetUtcNow is overridden: PeriodicTimer and Stopwatch-style timing must keep working
        // at real speed so the scheduler still ticks while the app clock is frozen.
        (DevTimeProvider clock, _) = Create();
        clock.SetNow(FixtureWednesday);
        clock.Freeze();

        long first = clock.GetTimestamp();
        long second = clock.GetTimestamp();

        second.Should().BeGreaterThanOrEqualTo(first);
        clock.TimestampFrequency.Should().BePositive();
    }

    private static (DevTimeProvider Clock, FixedTimeProvider Real) Create()
    {
        var real = new FixedTimeProvider(RealStart);
        return (new DevTimeProvider(real), real);
    }
}
