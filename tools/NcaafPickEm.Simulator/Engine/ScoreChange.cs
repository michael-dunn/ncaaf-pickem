using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>One scoreboard line the simulator wants a game to show.</summary>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="Status">The status to report.</param>
/// <param name="Home">Home points (ignored by the app while Scheduled).</param>
/// <param name="Away">Away points.</param>
/// <param name="Period">Quarter while in progress.</param>
/// <param name="Clock">Game clock while in progress.</param>
public readonly record struct ScoreChange(
    Guid GameId,
    GameStatus Status,
    int? Home,
    int? Away,
    byte? Period,
    string? Clock);
