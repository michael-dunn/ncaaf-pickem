namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// What one active game is frozen at (<c>04-Domain-Algorithms.md</c> sections 3 and 5). The
/// caller writes these onto the <c>WeekGameSetGames</c> row; nothing may change them afterwards.
/// </summary>
/// <param name="GameSetGameId">The row to write.</param>
/// <param name="SpreadAtLock">The newest line at lock, or null when the game had none.</param>
/// <param name="FrozenPointValue">
/// The final <c>ResolvedPointValue</c>: the override if there is one, else the first matching
/// point rule against <paramref name="SpreadAtLock"/>, else the league default.
/// </param>
public readonly record struct LockedGameSnapshot(
    Guid GameSetGameId,
    decimal? SpreadAtLock,
    int FrozenPointValue);
