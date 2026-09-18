using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// Season standings frozen at the moment a week became complete (D-007). Trend arrows compare
/// the latest two snapshots, so a later rescore of an earlier week does not move them.
/// </summary>
public sealed class SeasonStandingsSnapshot
{
    public Guid LeagueId { get; set; }

    public League? League { get; set; }

    /// <summary>The week this snapshot includes, inclusive.</summary>
    public int ThroughWeek { get; set; }

    public Guid MembershipId { get; set; }

    public Membership? Membership { get; set; }

    public int Rank { get; set; }

    public int TotalPoints { get; set; }
}
