using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>One row of <c>GET /api/seasons/{year}/weeks/{week}/games?search=</c>: a Saturday FBS game a commissioner may add manually.</summary>
/// <param name="GameId">The <c>Games.Id</c>.</param>
/// <param name="HomeTeam">Home team.</param>
/// <param name="AwayTeam">Away team.</param>
/// <param name="HomeRank">AP rank, when ranked.</param>
/// <param name="AwayRank">AP rank, when ranked.</param>
/// <param name="KickoffUtc">Scheduled kickoff.</param>
/// <param name="IsConferenceGame">Both teams in the same conference.</param>
public sealed record GameCandidate(
    Guid GameId,
    TeamDto HomeTeam,
    TeamDto AwayTeam,
    int? HomeRank,
    int? AwayRank,
    DateTimeOffset KickoffUtc,
    bool IsConferenceGame);
