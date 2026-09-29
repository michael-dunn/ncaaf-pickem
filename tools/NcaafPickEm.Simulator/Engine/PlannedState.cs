using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>What a game's scoreboard should read at one moment of its plan.</summary>
/// <param name="Status">Scheduled before kickoff, InProgress during, Final at the end.</param>
/// <param name="Home">Home points, or null before kickoff.</param>
/// <param name="Away">Away points, or null before kickoff.</param>
/// <param name="Period">Quarter 1-4 while in progress, else null.</param>
/// <param name="Clock">Game clock ("12:34") while in progress, else null.</param>
public readonly record struct PlannedState(
    GameStatus Status,
    int? Home,
    int? Away,
    byte? Period,
    string? Clock);
