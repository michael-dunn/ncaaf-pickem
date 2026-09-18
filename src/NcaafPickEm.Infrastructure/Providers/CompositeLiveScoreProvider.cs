using Microsoft.Extensions.Logging;
using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// The <see cref="ILiveScoreProvider"/> everything else depends on when a real provider is
/// configured. It asks <see cref="ILiveScoreHealth"/> which source is active, calls it, and
/// records the outcome, so the Saturday poller only ever calls one provider and never has to
/// know that a fallback exists (04-Domain-Algorithms.md section 10).
/// </summary>
public sealed class CompositeLiveScoreProvider : ILiveScoreProvider
{
    private readonly ILiveScoreProvider _espn;
    private readonly ILiveScoreProvider _cfbd;
    private readonly ILiveScoreHealth _health;
    private readonly ILogger<CompositeLiveScoreProvider> _logger;

    /// <summary>Creates the composite.</summary>
    /// <param name="espn">The ESPN provider.</param>
    /// <param name="cfbd">The CFBD fallback provider.</param>
    /// <param name="health">Failure tracking and source selection.</param>
    /// <param name="logger">Logger.</param>
    public CompositeLiveScoreProvider(
        ILiveScoreProvider espn,
        ILiveScoreProvider cfbd,
        ILiveScoreHealth health,
        ILogger<CompositeLiveScoreProvider> logger)
    {
        _espn = espn;
        _cfbd = cfbd;
        _health = health;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default)
    {
        LiveScoreSource source = _health.ActiveSource;

        try
        {
            IReadOnlyList<LiveScoreUpdate> updates = await Provider(source)
                .GetScoresAsync(easternDate, cancellationToken)
                .ConfigureAwait(false);
            _health.RecordSuccess();
            return updates;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "{Source} live scores failed for {EasternDate}", source, easternDate);
            _health.RecordFailure();

            LiveScoreSource now = _health.ActiveSource;
            if (now == source)
            {
                throw;
            }

            // That failure was the one that engaged the fallback. Serve this poll from the new
            // source rather than making the caller wait five minutes for the next tick.
            _logger.LogInformation("Retrying {EasternDate} against {Source}", easternDate, now);
            IReadOnlyList<LiveScoreUpdate> updates = await Provider(now)
                .GetScoresAsync(easternDate, cancellationToken)
                .ConfigureAwait(false);
            _health.RecordSuccess();
            return updates;
        }
    }

    private ILiveScoreProvider Provider(LiveScoreSource source) =>
        source == LiveScoreSource.Cfbd ? _cfbd : _espn;
}
