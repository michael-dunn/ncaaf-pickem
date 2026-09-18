using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>Body of <c>GET /api/leagues/{leagueId}</c>, <c>POST /api/leagues</c>, and invite accept.</summary>
/// <param name="LeagueId">The league.</param>
/// <param name="Name">League name.</param>
/// <param name="SeasonYear">The season.</param>
/// <param name="FirstWeek">First playable week.</param>
/// <param name="LastWeek">Last playable week.</param>
/// <param name="DefaultPointValue">Points for a correct pick when no rule or override applies (1..100).</param>
/// <param name="CurrentWeek">The week the season calendar says is current.</param>
/// <param name="IsComplete">True once the season is past <paramref name="LastWeek"/>.</param>
/// <param name="MyRole">The caller's role.</param>
/// <param name="MyCurrentWeekStatus">Caller's submission status for the current week, or null when it has no game set.</param>
/// <param name="CurrentWeekLockAtUtc">Lock instant of the current week's game set, or null when none.</param>
/// <param name="LockAtEasternDisplay">Eastern rendering of the lock instant, e.g. "Sat 12:00 PM ET".</param>
public sealed record LeagueDetail(
    Guid LeagueId,
    string Name,
    int SeasonYear,
    int FirstWeek,
    int LastWeek,
    int DefaultPointValue,
    int CurrentWeek,
    bool IsComplete,
    MembershipRole MyRole,
    SubmissionStatus? MyCurrentWeekStatus,
    DateTimeOffset? CurrentWeekLockAtUtc,
    string? LockAtEasternDisplay);
