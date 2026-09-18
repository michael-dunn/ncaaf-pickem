using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// One member's state for one week's game set (Feature 04). Derivation rules are in
/// <c>04-Domain-Algorithms.md</c> section 4.
/// </summary>
public sealed class WeekSubmission
{
    public Guid MembershipId { get; set; }

    public Membership? Membership { get; set; }

    public Guid WeekGameSetId { get; set; }

    public WeekGameSet? WeekGameSet { get; set; }

    public SubmissionStatus Status { get; set; }

    public DateTime? SubmittedUtc { get; set; }

    public DateTime LastChangedUtc { get; set; }

    /// <summary>Drives the "new games highlighted" treatment on the picks page.</summary>
    public bool HasUnseenGameChanges { get; set; }
}
