using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// A row already in the week's set, flattened from <c>WeekGameSetGames</c>. Drives the manual-add
/// step, the sticky-removal step, and the regeneration diff.
/// </summary>
/// <param name="GameId">The game the row points at.</param>
/// <param name="Source">How the game got into the set.</param>
/// <param name="IsRemoved">
/// True when the row was taken out before lock, by hand or by a schedule change (D-008 keeps the
/// row rather than deleting it). Sticky: the generator never puts a removed game back, whatever
/// the rules now say.
/// </param>
public sealed record ExistingSetGame(
    Guid GameId,
    GameSetGameSource Source,
    bool IsRemoved = false);
