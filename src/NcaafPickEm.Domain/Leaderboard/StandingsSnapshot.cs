namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>
/// One <c>SeasonStandingsSnapshots</c> row: where a member stood once a week became complete.
/// Trend arrows compare the two most recent snapshot weeks, so a later rescore of an earlier week
/// never moves them (D-007).
/// </summary>
/// <param name="ThroughWeek">The week this snapshot includes, inclusive.</param>
/// <param name="MembershipId">The membership.</param>
/// <param name="Rank">Competition rank at that point in the season.</param>
/// <param name="TotalPoints">Season total at that point.</param>
public sealed record StandingsSnapshot(
    int ThroughWeek,
    Guid MembershipId,
    int Rank,
    int TotalPoints);
