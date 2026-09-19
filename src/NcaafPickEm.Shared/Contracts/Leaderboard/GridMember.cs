namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>A column of the week grid.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="IsFormer">The member has since been removed.</param>
public sealed record GridMember(
    Guid MembershipId,
    string DisplayName,
    bool IsFormer);
