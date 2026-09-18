using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Leagues;

/// <summary>
/// One row of <c>GET /api/leagues</c>, and one entry of <c>MeResponse.Leagues</c>.
/// </summary>
/// <param name="LeagueId">The league.</param>
/// <param name="Name">League name.</param>
/// <param name="SeasonYear">The one season this league covers.</param>
/// <param name="MyRole">The caller's role in this league.</param>
/// <param name="CurrentWeek">The week the season calendar says is current.</param>
/// <param name="MyCurrentWeekStatus">
/// The caller's submission status for <paramref name="CurrentWeek"/>, or null when that week has
/// no game set yet.
/// </param>
public sealed record LeagueSummary(
    Guid LeagueId,
    string Name,
    int SeasonYear,
    MembershipRole MyRole,
    int CurrentWeek,
    SubmissionStatus? MyCurrentWeekStatus);
