using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>One cell of the week grid.</summary>
/// <param name="GameSetGameId">Row key.</param>
/// <param name="MembershipId">Column key.</param>
/// <param name="TeamId">The member's pick, or null.</param>
/// <param name="Outcome">Voided, NoPick, Pending, Correct, or Incorrect, in that precedence.</param>
public sealed record GridCell(
    Guid GameSetGameId,
    Guid MembershipId,
    Guid? TeamId,
    GridOutcome Outcome);
