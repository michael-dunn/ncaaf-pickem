namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Tracks whether the live-score source is working and which one is answering right now. A
/// singleton: the state is per process and per game day, not per request
/// (04-Domain-Algorithms.md section 10).
/// </summary>
/// <remarks>
/// Read by the dashboard (Feature 05's "scores may be stale" banner) and the data status page;
/// written by the composite provider on every call and by the Saturday poller at the start of a
/// new game day.
/// </remarks>
public interface ILiveScoreHealth
{
    /// <summary>What <c>Providers:LiveScores</c> asked for.</summary>
    LiveScoreSource ConfiguredSource { get; }

    /// <summary>What is actually being called, which differs once the fallback engages.</summary>
    LiveScoreSource ActiveSource { get; }

    /// <summary>Consecutive failures of the active source since its last success.</summary>
    int ConsecutiveFailures { get; }

    /// <summary>
    /// True once the fallback has engaged. The banner it drives must say more than "stale": the
    /// CFBD fallback has no period, no clock, no live odds and no Postponed or Cancelled (D-012).
    /// </summary>
    bool ScoresMayBeStale { get; }

    /// <summary>Records a successful call, clearing the failure streak.</summary>
    void RecordSuccess();

    /// <summary>
    /// Records a failed call. The third consecutive ESPN failure switches
    /// <see cref="ActiveSource"/> to <see cref="LiveScoreSource.Cfbd"/> for the rest of the day
    /// and sets <see cref="ScoresMayBeStale"/>.
    /// </summary>
    void RecordFailure();

    /// <summary>Returns to the configured source and clears the failure state. Called once per game day.</summary>
    void ResetForNewDay();
}
