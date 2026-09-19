namespace NcaafPickEm.Shared.Contracts.Dashboard;

/// <summary>
/// Body of <c>GET /api/leagues/{leagueId}/weeks/{week}/dashboard</c>. Before lock only
/// <paramref name="IsAvailable"/> (false), <paramref name="LockAtUtc"/> and
/// <paramref name="LockAtEasternDisplay"/> are meaningful; the lists are empty.
/// </summary>
/// <param name="IsAvailable">True once the week is locked.</param>
/// <param name="LockAtUtc">Lock instant, when a game set exists.</param>
/// <param name="LockAtEasternDisplay">Eastern rendering of the lock instant.</param>
/// <param name="PointsSoFar">Points already won by the viewer this week.</param>
/// <param name="MaxRemaining">Points still winnable on the viewer's undecided picks.</param>
/// <param name="ScoresMayBeStale">True while live scores come from the fallback source or the last poll failed.</param>
/// <param name="Games">Games with opposition, most influential first.</param>
/// <param name="EveryoneAgrees">Games where every member picked the same team as the viewer.</param>
public sealed record DashboardResponse(
    bool IsAvailable,
    DateTimeOffset? LockAtUtc,
    string? LockAtEasternDisplay,
    int PointsSoFar,
    int MaxRemaining,
    bool ScoresMayBeStale,
    DashboardGameDto[] Games,
    DashboardGameDto[] EveryoneAgrees);
