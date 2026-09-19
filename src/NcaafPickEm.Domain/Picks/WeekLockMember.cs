namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// One membership of the league as <see cref="WeekLocker"/> sees it, with everything needed to
/// decide whether it gets a row for the week and, if so, which status.
/// </summary>
/// <param name="MembershipId">The <c>Memberships.Id</c>.</param>
/// <param name="JoinedUtc">
/// When the member joined. A member who joined after the week froze never had a chance to pick,
/// so they get no row at all (<c>04-Domain-Algorithms.md</c> section 4).
/// </param>
/// <param name="IsActive"><c>RemovedUtc == null</c>: still a member of the league at lock.</param>
/// <param name="PickedGameSetGameIds">
/// Every <c>WeekGameSetGames.Id</c> the member holds a pick on. Picks on rows that are no longer
/// active are ignored, exactly as <see cref="SubmissionStatusCalculator"/> ignores them.
/// </param>
/// <param name="SubmittedUtc">When the member last pressed Submit for this week, or null.</param>
public sealed record WeekLockMember(
    Guid MembershipId,
    DateTime JoinedUtc,
    bool IsActive,
    IReadOnlyCollection<Guid> PickedGameSetGameIds,
    DateTime? SubmittedUtc);
