namespace NcaafPickEm.Shared.Contracts.Stats;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/members/{membershipId}/stats</c> (Feature 14).</summary>
/// <param name="MembershipId">The member.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="IsFormer">The member has since been removed; their categories carry no rank.</param>
/// <param name="IsMe">True when the caller is looking at their own profile.</param>
/// <param name="IsProvisional">True while one of the member's weeks with counted games is still
/// being played.</param>
/// <param name="Accuracy">Pick accuracy with the member's rank.</param>
/// <param name="Underdog">Underdog hits with the member's rank.</param>
/// <param name="MostCorrect">Complete week with the most correct picks; null before any.</param>
/// <param name="LeastCorrect">Complete week with the fewest correct picks.</param>
/// <param name="MostPoints">Complete week with the most points.</param>
/// <param name="LeastPoints">Complete week with the fewest points.</param>
/// <param name="Weeks">Every week the member played, oldest first.</param>
public sealed record MemberStatsProfile(
    Guid MembershipId,
    string DisplayName,
    bool IsFormer,
    bool IsMe,
    bool IsProvisional,
    MemberStat Accuracy,
    MemberStat Underdog,
    WeekHighlight? MostCorrect,
    WeekHighlight? LeastCorrect,
    WeekHighlight? MostPoints,
    WeekHighlight? LeastPoints,
    MemberWeekStat[] Weeks);
