using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.TeamForm;

/// <summary>One <see cref="FormGame"/> told from one team's side.</summary>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="OpponentTeamId">The other team.</param>
/// <param name="IsHome">True when the team was the home side.</param>
/// <param name="TeamScore">The team's score.</param>
/// <param name="OpponentScore">The opponent's score.</param>
/// <param name="Result">Win, loss or tie for the team.</param>
/// <param name="KickoffUtc">Kickoff, UTC.</param>
public sealed record FormEntry(
    Guid GameId,
    Guid OpponentTeamId,
    bool IsHome,
    int TeamScore,
    int OpponentScore,
    FormResult Result,
    DateTime KickoffUtc);
