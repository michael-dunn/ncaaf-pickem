using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>One provider update's match against the schedule.</summary>
/// <param name="Outcome">What the matcher concluded.</param>
/// <param name="Game">The matched game, or <see langword="null"/> for every other outcome.</param>
/// <param name="SidesSwapped">
/// True when the provider calls our away team the home team. ESPN labels a side "home" even at a
/// neutral site, and it does not always agree with CFBD, so the apply service must cross the
/// scores over rather than write the provider's home score into our home column.
/// </param>
/// <param name="Reason">A short human-readable explanation, for logs and the data status page.</param>
public sealed record GameMatchResult(
    GameMatchOutcome Outcome,
    Game? Game,
    bool SidesSwapped,
    string? Reason)
{
    /// <summary>True when a game was found, by either route.</summary>
    public bool IsMatch => Game is not null;
}
