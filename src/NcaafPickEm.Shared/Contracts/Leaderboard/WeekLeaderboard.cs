namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/leaderboard</c>.</summary>
/// <param name="Week">Week number.</param>
/// <param name="IsComplete">Every active game is final with a winner or voided; otherwise the client labels it provisional.</param>
/// <param name="Rows">Members with a result row this week, ordered by rank, then name.</param>
public sealed record WeekLeaderboard(
    int Week,
    bool IsComplete,
    WeekRow[] Rows);
