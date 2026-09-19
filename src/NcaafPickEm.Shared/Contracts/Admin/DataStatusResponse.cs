namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>
/// Everything the data status page shows (Features 09 and 12):
/// <c>GET /api/admin/data-status</c>.
/// </summary>
/// <param name="Refreshes">
/// One row per <see cref="Enums.RefreshDataType"/>, always all of them, with nulls for a slice
/// that has never been refreshed.
/// </param>
/// <param name="CfbdCallsThisMonth">
/// Calls to CollegeFootballData so far this UTC month, against the free tier's monthly allowance.
/// </param>
/// <param name="CfbdWarning">
/// True once <paramref name="CfbdCallsThisMonth" /> reaches the warning threshold of 800.
/// </param>
/// <param name="LiveScoreSource">The configured <c>Providers:LiveScores</c>: Espn, Cfbd, or Fixture.</param>
/// <param name="ActiveLiveScoreSource">
/// What is actually being called right now (<c>ILiveScoreHealth.ActiveSource</c>), which differs
/// from <paramref name="LiveScoreSource"/> once the ESPN-to-CFBD fallback has engaged
/// (04-Domain-Algorithms.md section 10).
/// </param>
/// <param name="ScoresMayBeStale">
/// True once the fallback has engaged: the CFBD fallback has no period, no clock, no live odds
/// and no Postponed or Cancelled (D-012).
/// </param>
/// <param name="Unmatched">Provider games still waiting to be matched to a <c>Games</c> row.</param>
/// <param name="NeedsReview">
/// Games in a league set that need a commissioner decision (P2-04 additive): a Final game with
/// no determinable winner (a tie or a missing score), and - P3-04 - a game inside an
/// already-locked week that has since been postponed or cancelled and has not yet been voided or
/// result-overridden. <c>Reason</c> says which ("Tie", "Missing score", "Postponed", "Cancelled");
/// the scores are null for the schedule-change rows, which never kicked off.
/// </param>
/// <param name="RecentJobs">The 50 most recent job runs, newest first.</param>
public sealed record DataStatusResponse(
    IReadOnlyList<RefreshStatusDto> Refreshes,
    int CfbdCallsThisMonth,
    bool CfbdWarning,
    string LiveScoreSource,
    IReadOnlyList<UnmatchedGameDto> Unmatched,
    IReadOnlyList<JobRunDto> RecentJobs,
    string ActiveLiveScoreSource = "",
    bool ScoresMayBeStale = false,
    IReadOnlyList<NeedsReviewGameDto>? NeedsReview = null)
{
    /// <summary>Never null on the wire; defaults to empty so older callers deserialize safely.</summary>
    public IReadOnlyList<NeedsReviewGameDto> NeedsReview { get; init; } = NeedsReview ?? [];
}
