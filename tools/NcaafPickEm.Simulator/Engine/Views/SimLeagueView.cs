namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>A league of the simulated season.</summary>
/// <param name="LeagueId">The league.</param>
/// <param name="Name">Its name.</param>
/// <param name="FirstWeek">First playable week.</param>
/// <param name="LastWeek">Last playable week.</param>
/// <param name="IsComplete">Stored completion flag.</param>
/// <param name="Set">This week's set, or null before one exists.</param>
/// <param name="Members">Active members, commissioners first.</param>
public sealed record SimLeagueView(
    Guid LeagueId,
    string Name,
    int FirstWeek,
    int LastWeek,
    bool IsComplete,
    SimSetView? Set,
    IReadOnlyList<SimMemberView> Members);
