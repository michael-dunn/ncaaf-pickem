using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>
/// Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/picks</c>: everybody's picks, available
/// only once the week has locked (403 before that).
/// </summary>
/// <param name="Games">Active games ordered by kickoff.</param>
/// <param name="Members">
/// Every membership that was active at lock — that is, every one holding a <c>WeekSubmissions</c>
/// row — including members who have since left the league.
/// </param>
public sealed record WeekPicksResponse(
    GameSetGameDto[] Games,
    MemberPicksRow[] Members);
