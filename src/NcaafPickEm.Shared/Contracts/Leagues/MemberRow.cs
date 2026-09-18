using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>One row of <c>GET /api/leagues/{leagueId}/members</c>.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league name (override, else the user's global name).</param>
/// <param name="Role">Member or Commissioner.</param>
/// <param name="JoinedWeek">Week that was current when the member joined.</param>
/// <param name="IsFormer">True when the member was removed; kept for history.</param>
/// <param name="CurrentWeekStatus">Current-week submission status; populated only for commissioner callers.</param>
public sealed record MemberRow(
    Guid MembershipId,
    string DisplayName,
    MembershipRole Role,
    int JoinedWeek,
    bool IsFormer,
    SubmissionStatus? CurrentWeekStatus);
