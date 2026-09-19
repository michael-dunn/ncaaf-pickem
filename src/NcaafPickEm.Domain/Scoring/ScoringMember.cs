using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// One membership that holds a <c>WeekSubmissions</c> row for the locked week, as
/// <see cref="WeekScorer"/> sees it.
/// </summary>
/// <remarks>
/// The caller lists every row the week has, whatever its status, and the scorer keeps only
/// <see cref="SubmissionStatus.Locked"/> and <see cref="SubmissionStatus.Incomplete"/> - the two
/// the lock job writes (<c>04-Domain-Algorithms.md</c> section 5). That query is the whole of
/// "who was active at lock" (D-111): it already includes a member removed since lock, whose
/// pre-lock row is deliberately left in place, and already excludes anyone who joined after it.
/// A row still reading <c>NotStarted</c>/<c>InProgress</c>/<c>Submitted</c> belongs to a week the
/// lock job has not settled, so it is not a scoreable week yet.
/// </remarks>
/// <param name="MembershipId">The membership.</param>
/// <param name="Status">Its <c>WeekSubmissions.Status</c> for this week.</param>
public readonly record struct ScoringMember(Guid MembershipId, SubmissionStatus Status)
{
    /// <summary>Whether the lock job has settled this row, and it therefore earns a result.</summary>
    public bool WasActiveAtLock =>
        Status is SubmissionStatus.Locked or SubmissionStatus.Incomplete;
}
