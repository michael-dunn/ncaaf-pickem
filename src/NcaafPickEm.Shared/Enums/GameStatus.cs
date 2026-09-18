namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// Lifecycle of a single <c>Games</c> row, driven by the live score poller.
/// </summary>
public enum GameStatus : byte
{
    /// <summary>Kickoff has not happened yet.</summary>
    Scheduled = 0,

    /// <summary>Underway; <c>Period</c> and <c>Clock</c> are meaningful.</summary>
    InProgress = 1,

    /// <summary>Over. Scores are authoritative and the game can be scored.</summary>
    Final = 2,

    /// <summary>Moved to a later date by the schedule owner.</summary>
    Postponed = 3,

    /// <summary>Will not be played.</summary>
    Cancelled = 4,
}
