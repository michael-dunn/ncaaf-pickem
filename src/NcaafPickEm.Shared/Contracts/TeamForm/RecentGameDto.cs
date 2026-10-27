using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.TeamForm;

/// <summary>One of a team's recent games, told from that team's side (Feature 17).</summary>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="Opponent">The other team.</param>
/// <param name="IsHome">True when the team was the home side ("vs"), false when away ("@").</param>
/// <param name="TeamScore">The team's final score.</param>
/// <param name="OpponentScore">The opponent's final score.</param>
/// <param name="Result">Win, loss or tie for the team.</param>
/// <param name="KickoffUtc">When the game kicked off.</param>
public sealed record RecentGameDto(
    Guid GameId,
    TeamDto Opponent,
    bool IsHome,
    int TeamScore,
    int OpponentScore,
    FormResult Result,
    DateTimeOffset KickoffUtc);
