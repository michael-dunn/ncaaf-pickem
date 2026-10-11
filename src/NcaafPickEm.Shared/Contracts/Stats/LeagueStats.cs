namespace NcaafPickEm.Shared.Contracts.Stats;

/// <summary>Body of <c>GET /api/leagues/{leagueId}/stats</c> (Feature 14).</summary>
/// <param name="ThroughWeek">Latest week with a counted game; null before any game is counted,
/// which is the page's empty state.</param>
/// <param name="IsProvisional">True while a week with counted games is still being played; the
/// client labels the values "In Progress", as the weekly leaderboard does.</param>
/// <param name="Accuracy">Pick accuracy: every active member, ranked by percentage.</param>
/// <param name="Underdog">Underdog hits: every active member, ranked by percentage.</param>
public sealed record LeagueStats(
    int? ThroughWeek,
    bool IsProvisional,
    StatRow[] Accuracy,
    StatRow[] Underdog);
