using NcaafPickEm.Infrastructure.Providers;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A settable <see cref="ILiveScoreHealth"/> for testing the dashboard's "scores may be stale"
/// banner (P6-02) without three real ESPN failures.
/// </summary>
/// <remarks>
/// Register with the <c>ApiFactory</c> <c>configureServices</c> hook: <c>services.RemoveAll&lt;ILiveScoreHealth&gt;()</c>
/// then <c>services.AddSingleton&lt;ILiveScoreHealth&gt;(fake)</c>, the same pattern
/// <c>FakePushSender</c> uses for <c>IPushSender</c>.
/// </remarks>
public sealed class FakeLiveScoreHealth : ILiveScoreHealth
{
    /// <inheritdoc />
    public LiveScoreSource ConfiguredSource { get; init; } = LiveScoreSource.Fixture;

    /// <inheritdoc />
    public LiveScoreSource ActiveSource { get; init; } = LiveScoreSource.Fixture;

    /// <inheritdoc />
    public int ConsecutiveFailures { get; init; }

    /// <summary>Settable directly, for a test to flip the dashboard's stale banner on and off.</summary>
    public bool ScoresMayBeStale { get; set; }

    /// <inheritdoc />
    public void RecordSuccess()
    {
    }

    /// <inheritdoc />
    public void RecordFailure()
    {
    }

    /// <inheritdoc />
    public void ResetForNewDay()
    {
    }
}
