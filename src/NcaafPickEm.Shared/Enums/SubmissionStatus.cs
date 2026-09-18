namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// A member's state for one week's game set. Derivation rules live in
/// <c>04-Domain-Algorithms.md</c> section 4.
/// </summary>
public enum SubmissionStatus : byte
{
    /// <summary>No picks made on active games.</summary>
    NotStarted = 0,

    /// <summary>Some but not all active games picked, or a new game arrived after Submit.</summary>
    InProgress = 1,

    /// <summary>Every active game picked and Submit pressed since the last game was added.</summary>
    Submitted = 2,

    /// <summary>Written by the lock job for a member who was <see cref="Submitted"/> at lock.</summary>
    Locked = 3,

    /// <summary>Written by the lock job for a member who was not <see cref="Submitted"/> at lock.</summary>
    Incomplete = 4,
}
