using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/grid</c>; 403 before lock.</summary>
/// <param name="Games">Active games in kickoff order (voided included, flagged on the game).</param>
/// <param name="Members">Members with a submission row for the week.</param>
/// <param name="Cells">One cell per game per member.</param>
public sealed record WeekGrid(
    GameSetGameDto[] Games,
    GridMember[] Members,
    GridCell[] Cells);
