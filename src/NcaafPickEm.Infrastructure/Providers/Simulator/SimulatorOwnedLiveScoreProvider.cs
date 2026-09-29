using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Infrastructure.Providers.Simulator;

/// <summary>
/// The app's live-score provider when <c>Providers:LiveScores</c> is <c>Simulator</c> (P11-01,
/// D-183): always empty. <c>tools/NcaafPickEm.Simulator</c> is the scoreboard - it feeds its own
/// updates through <c>SaturdayPoller.PollOnceAsync</c> in its own process - so the app has
/// nothing to poll and does not register <c>SaturdayPoller</c> in this mode.
/// </summary>
/// <remarks>
/// The only caller left in the app is the data page's manual "Scores" refresh, for which an
/// empty answer is the honest one: applying nothing changes nothing, and the refresh still
/// records a successful attempt.
/// </remarks>
public sealed class SimulatorOwnedLiveScoreProvider : ILiveScoreProvider
{
    /// <inheritdoc />
    public Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LiveScoreUpdate>>([]);
}
