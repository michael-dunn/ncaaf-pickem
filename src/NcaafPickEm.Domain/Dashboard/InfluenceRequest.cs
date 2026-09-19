namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// Everything <see cref="InfluenceCalculator"/> needs to build one member's dashboard for one
/// locked week (Feature 05, <c>04-Domain-Algorithms.md</c> section 6).
/// </summary>
/// <param name="ViewerMembershipId">The member whose dashboard this is. They never appear in any of
/// their own lists, and the dashboard is only ever their own - the story rules out viewing it
/// "as" somebody else.</param>
/// <param name="Games">The games in the set. Voided games may be passed in and are dropped by the
/// calculator; games removed before lock should not be here at all, since the story's set is the
/// one that locked.</param>
/// <param name="MembersActiveAtLock">
/// Every membership that was active when the week locked, in the order their names should be
/// listed. The service decides membership of this list - "has a <c>WeekSubmissions</c> row for this
/// set" - which is what includes a member who has since been removed and excludes one who joined
/// after lock. The calculator never looks beyond it: a pick from a membership that is not listed is
/// ignored, so a post-lock joiner cannot appear even if their pick rows are passed in.
/// </param>
/// <param name="Picks">Picks by those members on those games, in any order.</param>
public sealed record InfluenceRequest(
    Guid ViewerMembershipId,
    IReadOnlyList<InfluenceGame> Games,
    IReadOnlyList<InfluenceMember> MembersActiveAtLock,
    IReadOnlyList<InfluencePick> Picks);
