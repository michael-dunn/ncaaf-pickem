using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// The games one league plays in one week, plus that week's lock state (Features 02, 04).
/// </summary>
public sealed class WeekGameSet
{
    public Guid Id { get; set; }

    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    public int Week { get; set; }

    /// <summary>When set, generation uses this week's override rules instead of the default set.</summary>
    public bool UsesOverride { get; set; }

    public DateTime GeneratedUtc { get; set; }

    /// <summary>Earliest Saturday kickoff among active games; null when the set has none.</summary>
    public DateTime? LockAtUtc { get; set; }

    /// <summary>Written once by the lock job. Everything about the set is frozen after this.</summary>
    public DateTime? LockedUtc { get; set; }

    /// <summary>True once every active game is Final or voided.</summary>
    public bool IsComplete { get; set; }

    /// <summary>True once the lock job has run for this week.</summary>
    public bool IsLocked => LockedUtc is not null;
}
