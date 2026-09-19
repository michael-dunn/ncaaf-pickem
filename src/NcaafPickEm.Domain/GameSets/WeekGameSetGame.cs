using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.GameSets;

/// <summary>
/// One game inside one league's week (Features 02, 03, 06). Rows are never deleted: a game taken
/// out before lock is flagged <see cref="IsRemoved"/> (D-008) and one taken out after lock is
/// flagged <see cref="IsVoided"/>, so picks, history, and notifications keep a row to point at.
/// </summary>
public sealed class WeekGameSetGame
{
    /// <summary>Maximum length of <see cref="RemovedReason"/>, in characters.</summary>
    public const int RemovedReasonMaxLength = 200;

    public Guid Id { get; set; }

    public Guid WeekGameSetId { get; set; }

    public WeekGameSet? WeekGameSet { get; set; }

    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    public GameSetGameSource Source { get; set; }

    /// <summary>
    /// When this game joined the set: set on insert and again whenever a removed row is
    /// re-activated. It is what "the last time TotalCount increased" means in
    /// <c>04-Domain-Algorithms.md</c> section 4, so a game added after a member pressed Submit
    /// reverts them to In Progress (D-083).
    /// </summary>
    public DateTime AddedUtc { get; set; }

    /// <summary>Removed before lock, by hand or by a schedule change. Sticky across regeneration.</summary>
    public bool IsRemoved { get; set; }

    public string? RemovedReason { get; set; }

    /// <summary>Commissioner's manual point value. Null means the rules decide.</summary>
    public int? PointValueOverride { get; set; }

    /// <summary>Computed on generate and edit, frozen at lock.</summary>
    public int ResolvedPointValue { get; set; }

    /// <summary>Snapshot of the current spread taken at lock, for the record.</summary>
    public decimal? SpreadAtLock { get; set; }

    /// <summary>Voided after lock, so the game scores for nobody (Feature 06).</summary>
    public bool IsVoided { get; set; }

    /// <summary>Commissioner's correction of the winner, overriding the provider result.</summary>
    public Guid? ResultOverrideWinnerTeamId { get; set; }

    /// <summary>A game counts for picks and scoring only while it is neither removed nor voided.</summary>
    public bool IsActive => !IsRemoved && !IsVoided;
}
