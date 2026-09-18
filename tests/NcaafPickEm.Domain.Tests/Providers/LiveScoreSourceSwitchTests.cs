using Microsoft.Extensions.Logging.Abstractions;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Providers;

/// <summary>
/// The ESPN-to-CFBD runtime fallback (P2-03, 04-Domain-Algorithms.md section 10): three
/// consecutive ESPN failures switch the active source for the rest of the day and raise the
/// "scores may be stale" flag; a success clears the streak; the fixture source never switches.
/// </summary>
public sealed class LiveScoreSourceSwitchTests
{
    private static readonly DateOnly Saturday = new(2026, 10, 17);

    [Fact]
    public async Task GivenEspnFailsThreeTimesInARow_WhenPolling_ThenCfbdTakesOverAndScoresAreFlaggedStale()
    {
        LiveScoreHealth health = Health(LiveScoreSource.Espn);
        var espn = new FakeProvider { Throws = true };
        var cfbd = new FakeProvider();
        CompositeLiveScoreProvider composite = Composite(espn, cfbd, health);

        await Invoking(composite).Should().ThrowAsync<HttpRequestException>();
        await Invoking(composite).Should().ThrowAsync<HttpRequestException>();

        // The third failure is the one that engages the fallback, and the same poll is served
        // from CFBD rather than being lost.
        IReadOnlyList<LiveScoreUpdate> updates = await composite.GetScoresAsync(Saturday);

        updates.Should().HaveCount(1);
        cfbd.Calls.Should().Be(1);
        health.ActiveSource.Should().Be(LiveScoreSource.Cfbd);
        health.ScoresMayBeStale.Should().BeTrue();
    }

    [Fact]
    public async Task GivenTwoFailuresThenASuccess_WhenPolling_ThenTheStreakResetsAndEspnKeepsAnswering()
    {
        LiveScoreHealth health = Health(LiveScoreSource.Espn);
        var espn = new FakeProvider { Throws = true };
        var cfbd = new FakeProvider();
        CompositeLiveScoreProvider composite = Composite(espn, cfbd, health);

        await Invoking(composite).Should().ThrowAsync<HttpRequestException>();
        await Invoking(composite).Should().ThrowAsync<HttpRequestException>();
        health.ConsecutiveFailures.Should().Be(2);

        espn.Throws = false;
        await composite.GetScoresAsync(Saturday);

        health.ConsecutiveFailures.Should().Be(0);
        health.ActiveSource.Should().Be(LiveScoreSource.Espn);
        health.ScoresMayBeStale.Should().BeFalse();
        cfbd.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GivenFailuresAfterTheFallbackEngaged_WhenPolling_ThenCfbdFailuresJustPropagate()
    {
        LiveScoreHealth health = Health(LiveScoreSource.Espn);
        var espn = new FakeProvider { Throws = true };
        var cfbd = new FakeProvider { Throws = true };
        CompositeLiveScoreProvider composite = Composite(espn, cfbd, health);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Invoking(composite).Should().ThrowAsync<HttpRequestException>();
        }

        health.ActiveSource.Should().Be(LiveScoreSource.Cfbd);

        // CFBD is the last stop; there is nothing to fall back to, so the poller sees the error.
        await Invoking(composite).Should().ThrowAsync<HttpRequestException>();
        health.ActiveSource.Should().Be(LiveScoreSource.Cfbd);
    }

    [Fact]
    public void GivenTheFixtureSource_WhenItFailsRepeatedly_ThenNothingSwitchesAndNothingIsStale()
    {
        LiveScoreHealth health = Health(LiveScoreSource.Fixture);

        for (int failure = 0; failure < 5; failure++)
        {
            health.RecordFailure();
        }

        health.ActiveSource.Should().Be(LiveScoreSource.Fixture);
        health.ScoresMayBeStale.Should().BeFalse();
    }

    [Fact]
    public void GivenCfbdIsTheConfiguredSource_WhenItFailsRepeatedly_ThenThereIsNowhereToSwitchTo()
    {
        LiveScoreHealth health = Health(LiveScoreSource.Cfbd);

        for (int failure = 0; failure < 5; failure++)
        {
            health.RecordFailure();
        }

        health.ActiveSource.Should().Be(LiveScoreSource.Cfbd);
        health.ScoresMayBeStale.Should().BeFalse();
        health.ConsecutiveFailures.Should().Be(5);
    }

    [Fact]
    public void GivenTheFallbackEngaged_WhenANewGameDayStarts_ThenTheConfiguredSourceComesBack()
    {
        LiveScoreHealth health = Health(LiveScoreSource.Espn);

        health.RecordFailure();
        health.RecordFailure();
        health.RecordFailure();
        health.ActiveSource.Should().Be(LiveScoreSource.Cfbd);

        health.ResetForNewDay();

        health.ActiveSource.Should().Be(LiveScoreSource.Espn);
        health.ScoresMayBeStale.Should().BeFalse();
        health.ConsecutiveFailures.Should().Be(0);
    }

    private static LiveScoreHealth Health(LiveScoreSource configured) =>
        new(configured, NullLogger<LiveScoreHealth>.Instance);

    private static CompositeLiveScoreProvider Composite(
        ILiveScoreProvider espn,
        ILiveScoreProvider cfbd,
        ILiveScoreHealth health) =>
        new(espn, cfbd, health, NullLogger<CompositeLiveScoreProvider>.Instance);

    private static Func<Task<IReadOnlyList<LiveScoreUpdate>>> Invoking(CompositeLiveScoreProvider composite) =>
        () => composite.GetScoresAsync(Saturday);

    private sealed class FakeProvider : ILiveScoreProvider
    {
        public bool Throws { get; set; }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
            DateOnly easternDate,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            if (Throws)
            {
                throw new HttpRequestException("provider is down");
            }

            IReadOnlyList<LiveScoreUpdate> updates =
            [
                new LiveScoreUpdate(
                    "1",
                    new DateTime(2026, 10, 17, 19, 30, 0, DateTimeKind.Utc),
                    "Michigan",
                    "MICH",
                    null,
                    null,
                    "Texas",
                    "TEX",
                    null,
                    null,
                    GameStatus.Scheduled,
                    "STATUS_SCHEDULED",
                    false,
                    null,
                    null,
                    null),
            ];

            return Task.FromResult(updates);
        }
    }
}
