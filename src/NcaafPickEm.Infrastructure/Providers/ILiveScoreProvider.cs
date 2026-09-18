using NcaafPickEm.Infrastructure.Providers.Models;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Live game status and scores for one Eastern calendar date (Feature 12). ESPN's own scoreboard
/// buckets by Eastern date, not UTC, and only accepts one date per call (D-012), which is why the
/// contract is shaped this way rather than by a UTC range.
/// </summary>
public interface ILiveScoreProvider
{
    /// <summary>Every game update the provider has for <paramref name="easternDate"/>.</summary>
    Task<IReadOnlyList<LiveScoreUpdate>> GetScoresAsync(
        DateOnly easternDate,
        CancellationToken cancellationToken = default);
}
