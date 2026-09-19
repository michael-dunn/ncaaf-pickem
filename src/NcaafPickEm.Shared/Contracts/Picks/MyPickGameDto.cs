using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>
/// One game on the member's own picks page: the shared game projection plus the two facts only
/// the picks page needs. Composed rather than flattened so the game shape stays in one place
/// (D-069), the same way <c>DashboardGameDto</c> carries its game.
/// </summary>
/// <param name="Game">The game, exactly as every other game-bearing endpoint renders it.</param>
/// <param name="MyTeamId">The team the caller picked, or null when they have not picked yet.</param>
/// <param name="IsNewSinceSubmit">
/// True when the game joined the set after the caller last pressed Submit, so the UI can ribbon it
/// as new (Feature 04).
/// </param>
public sealed record MyPickGameDto(
    GameSetGameDto Game,
    Guid? MyTeamId,
    bool IsNewSinceSubmit);
