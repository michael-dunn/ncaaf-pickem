namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A league's set for the current week.</summary>
/// <param name="SetId">The <c>WeekGameSets.Id</c>.</param>
/// <param name="LockAtUtc">First kickoff among its games.</param>
/// <param name="LockedUtc">When the lock job ran, or null.</param>
/// <param name="IsFrozen">Picks are closed: locked, or past the lock instant.</param>
/// <param name="IsComplete">Every game final (or voided) and scored.</param>
/// <param name="Games">Its games, removed ones included.</param>
public sealed record SimSetView(
    Guid SetId,
    DateTimeOffset? LockAtUtc,
    DateTimeOffset? LockedUtc,
    bool IsFrozen,
    bool IsComplete,
    IReadOnlyList<SimSetGameView> Games)
{
    /// <summary>The set's games that count: not removed, not voided.</summary>
    public IEnumerable<SimSetGameView> ActiveGames => Games.Where(game => !game.IsRemoved && !game.IsVoided);
}
