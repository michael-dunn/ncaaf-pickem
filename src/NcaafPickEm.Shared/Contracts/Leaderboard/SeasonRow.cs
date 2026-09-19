using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Leaderboard;

/// <summary>One row of the season leaderboard (competition ranking: ties share a rank, the next rank skips).</summary>
/// <param name="Rank">1-based competition rank.</param>
/// <param name="MembershipId">The membership.</param>
/// <param name="DisplayName">Effective per-league name.</param>
/// <param name="TotalPoints">Sum of week points across the league's weeks.</param>
/// <param name="PointsBehind">Leader total minus this total; 0 for the leader.</param>
/// <param name="WeeklyWins">Complete weeks where this member had (or tied) the week's high score.</param>
/// <param name="Trend">Movement versus the previous complete week's snapshot.</param>
/// <param name="IsMe">True for the caller's row.</param>
public sealed record SeasonRow(
    int Rank,
    Guid MembershipId,
    string DisplayName,
    int TotalPoints,
    int PointsBehind,
    int WeeklyWins,
    StandingsTrend Trend,
    bool IsMe);
