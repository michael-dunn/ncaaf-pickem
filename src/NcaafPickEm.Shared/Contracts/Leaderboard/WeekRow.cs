namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>One row of a week leaderboard; includes former members who had a result that week.</summary>
/// <param name="Rank">1-based competition rank by points.</param>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="Points">Points earned this week.</param>
/// <param name="Correct">Correct picks.</param>
/// <param name="Total">Active (non-voided) games in the set.</param>
/// <param name="IsWinner">Points equal the week's maximum and the week is complete.</param>
/// <param name="IsFormer">The member has since been removed.</param>
/// <param name="IsMe">True for the caller's row.</param>
public sealed record WeekRow(
    int Rank,
    Guid MembershipId,
    string DisplayName,
    int Points,
    int Correct,
    int Total,
    bool IsWinner,
    bool IsFormer,
    bool IsMe);
