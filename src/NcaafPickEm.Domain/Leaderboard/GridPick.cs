namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>One member's pick on one game of the week grid.</summary>
/// <param name="MembershipId">Whose pick it is. A pick from a membership the caller did not list
/// is ignored.</param>
/// <param name="GameSetGameId">The game inside the set.</param>
/// <param name="TeamId">The team they picked.</param>
public sealed record GridPick(
    Guid MembershipId,
    Guid GameSetGameId,
    Guid TeamId);
