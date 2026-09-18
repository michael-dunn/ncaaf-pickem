using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// A member's score for one week, materialized and recomputed in full on every scoring event
/// (D-006). Never updated incrementally.
/// </summary>
public sealed class WeekResult
{
    public Guid MembershipId { get; set; }

    public Membership? Membership { get; set; }

    public Guid WeekGameSetId { get; set; }

    public WeekGameSet? WeekGameSet { get; set; }

    public int Points { get; set; }

    public int CorrectCount { get; set; }

    /// <summary>Active games in the set when this row was computed.</summary>
    public int ActiveGameCount { get; set; }

    /// <summary>Copy of <c>WeekGameSets.IsComplete</c> at compute time.</summary>
    public bool IsWeekComplete { get; set; }

    public DateTime ComputedUtc { get; set; }
}
