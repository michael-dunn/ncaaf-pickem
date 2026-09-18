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
/// <param name="Unmatched">Provider games still waiting to be matched to a <c>Games</c> row.</param>
/// <param name="RecentJobs">The 50 most recent job runs, newest first.</param>
public sealed record DataStatusResponse(
    IReadOnlyList<RefreshStatusDto> Refreshes,
    int CfbdCallsThisMonth,
    bool CfbdWarning,
    string LiveScoreSource,
    IReadOnlyList<UnmatchedGameDto> Unmatched,
    IReadOnlyList<JobRunDto> RecentJobs);
