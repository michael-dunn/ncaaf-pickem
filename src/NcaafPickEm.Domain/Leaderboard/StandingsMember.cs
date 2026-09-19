namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>
/// One member the standings can name. The caller decides which memberships are in the list: the
/// season leaderboard passes the active ones, a week leaderboard passes everybody who has a result
/// row for that week, former members included.
/// </summary>
/// <param name="MembershipId">The membership. Results and snapshots are matched on this.</param>
/// <param name="DisplayName">The name to show - the league nickname when set, else the global one
/// (<c>MemberNameProjection</c>). Resolving it is the caller's job.</param>
/// <param name="IsFormer">True when the membership has since been removed from the league.</param>
/// <param name="JoinedWeek">The week the member joined. Nothing before it counts towards their
/// season total, so a mid-season joiner is measured on their own weeks only.</param>
public sealed record StandingsMember(
    Guid MembershipId,
    string DisplayName,
    bool IsFormer,
    int JoinedWeek);
