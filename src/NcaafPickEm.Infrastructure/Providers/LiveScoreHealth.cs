using Microsoft.Extensions.Logging;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>The only <see cref="ILiveScoreHealth"/>. Thread-safe; a singleton.</summary>
public sealed class LiveScoreHealth : ILiveScoreHealth
{
    /// <summary>Consecutive ESPN failures that engage the CFBD fallback.</summary>
    public const int FailureThreshold = 3;

    private readonly ILogger<LiveScoreHealth> _logger;
    private readonly Lock _gate = new();

    private LiveScoreSource _activeSource;
    private int _consecutiveFailures;
    private bool _scoresMayBeStale;

    /// <summary>Creates the tracker.</summary>
    /// <param name="configuredSource">What <c>Providers:LiveScores</c> asked for.</param>
    /// <param name="logger">Logger.</param>
    public LiveScoreHealth(LiveScoreSource configuredSource, ILogger<LiveScoreHealth> logger)
    {
        ConfiguredSource = configuredSource;
        _activeSource = configuredSource;
        _logger = logger;
    }

    /// <inheritdoc />
    public LiveScoreSource ConfiguredSource { get; }

    /// <inheritdoc />
    public LiveScoreSource ActiveSource
    {
        get
        {
            lock (_gate)
            {
                return _activeSource;
            }
        }
    }

    /// <inheritdoc />
    public int ConsecutiveFailures
    {
        get
        {
            lock (_gate)
            {
                return _consecutiveFailures;
            }
        }
    }

    /// <inheritdoc />
    public bool ScoresMayBeStale
    {
        get
        {
            lock (_gate)
            {
                return _scoresMayBeStale;
            }
        }
    }

    /// <inheritdoc />
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
        }
    }

    /// <inheritdoc />
    public void RecordFailure()
    {
        bool switched = false;

        lock (_gate)
        {
            _consecutiveFailures++;

            // Only ESPN has somewhere to fall back to, and the switch holds for the rest of the
            // day: flapping between two sources mid-Saturday would make the scores less
            // trustworthy, not more (04-Domain-Algorithms.md section 10).
            if (_consecutiveFailures >= FailureThreshold && _activeSource == LiveScoreSource.Espn)
            {
                _activeSource = LiveScoreSource.Cfbd;
                _scoresMayBeStale = true;
                _consecutiveFailures = 0;
                switched = true;
            }
        }

        if (switched)
        {
            _logger.LogWarning(
                "ESPN failed {FailureThreshold} times in a row; falling back to CFBD for the rest of the day. " +
                "Live period, clock, odds and Postponed/Cancelled are unavailable until the next reset",
                FailureThreshold);
        }
    }

    /// <inheritdoc />
    public void ResetForNewDay()
    {
        bool wasFallenBack;

        lock (_gate)
        {
            wasFallenBack = _activeSource != ConfiguredSource;
            _activeSource = ConfiguredSource;
            _consecutiveFailures = 0;
            _scoresMayBeStale = false;
        }

        if (wasFallenBack)
        {
            _logger.LogInformation("Live score source reset to {ConfiguredSource} for a new game day", ConfiguredSource);
        }
    }
}
