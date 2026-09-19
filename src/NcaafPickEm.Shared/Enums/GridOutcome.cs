namespace NcaafPickEm.Shared.Enums;

/// <summary>Outcome of one member's pick on one game in the week grid (04-Domain-Algorithms section 8).</summary>
public enum GridOutcome : byte
{
    /// <summary>No determinable winner yet.</summary>
    Pending = 0,

    /// <summary>Pick matches the winner.</summary>
    Correct = 1,

    /// <summary>Pick does not match the winner.</summary>
    Incorrect = 2,

    /// <summary>The member made no pick on this game.</summary>
    NoPick = 3,

    /// <summary>The game was voided after lock; nothing scores.</summary>
    Voided = 4,
}
