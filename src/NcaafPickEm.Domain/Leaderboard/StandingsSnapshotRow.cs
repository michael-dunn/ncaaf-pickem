namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>
/// One row of <see cref="StandingsCalculator.ComputeSnapshot"/>: exactly what the snapshot writer
/// persists into <c>SeasonStandingsSnapshots</c> for one <c>ThroughWeek</c>.
/// </summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="Rank">Competition rank through that week.</param>
/// <param name="TotalPoints">Season total through that week.</param>
public sealed record StandingsSnapshotRow(
    Guid MembershipId,
    int Rank,
    int TotalPoints);
