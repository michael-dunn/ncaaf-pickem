namespace NcaafPickEm.Shared.Contracts.Dashboard;

/// <summary>A member named on the influence dashboard.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league display name.</param>
/// <param name="IsFormer">True when the member has since been removed but was active at lock.</param>
public sealed record MemberRef(
    Guid MembershipId,
    string DisplayName,
    bool IsFormer);
