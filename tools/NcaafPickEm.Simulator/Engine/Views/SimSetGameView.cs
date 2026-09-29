using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A game inside a league's set.</summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c>.</param>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="PointValue">Resolved point value.</param>
/// <param name="Source">Rule or manual.</param>
/// <param name="IsRemoved">Removed before lock.</param>
/// <param name="IsVoided">Voided after lock.</param>
public sealed record SimSetGameView(
    Guid GameSetGameId,
    Guid GameId,
    int PointValue,
    GameSetGameSource Source,
    bool IsRemoved,
    bool IsVoided);
