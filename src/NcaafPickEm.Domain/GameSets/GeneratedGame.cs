using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// One game the generator selected, with the source the persisted row should carry.
/// </summary>
/// <param name="GameId">The selected game.</param>
/// <param name="Source">
/// <see cref="GameSetGameSource.Manual"/> when the game is already in the set as a manual add,
/// even if a rule now selects it too, so the row stays immune to later rule regeneration.
/// Otherwise <see cref="GameSetGameSource.Rule"/>.
/// </param>
public sealed record GeneratedGame(Guid GameId, GameSetGameSource Source);
