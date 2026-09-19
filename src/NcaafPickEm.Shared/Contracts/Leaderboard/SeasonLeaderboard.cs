namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/leaderboard</c>.</summary>
/// <param name="ThroughWeek">Latest week with any results counted; null before the first scored week.</param>
/// <param name="Rows">Active members ordered by rank, then name.</param>
public sealed record SeasonLeaderboard(
    int? ThroughWeek,
    SeasonRow[] Rows);
