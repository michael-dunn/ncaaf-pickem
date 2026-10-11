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
/// <param name="Landing">
/// The screen the app opens this league on: Picks, the Dashboard, the Leaderboard, or the league
/// home (04-Domain-Algorithms.md section 12).
/// </param>
/// <param name="DisplayWeek">
/// The week the app treats as "this week" (Feature 15, 04-Domain-Algorithms.md section 12.1): the
/// week just played from the Sunday rollover until the next week's picks open on Tuesday,
/// otherwise <paramref name="CurrentWeek"/> clamped to the league's range.
/// </param>
public sealed record LeagueSummary(
    Guid LeagueId,
    string Name,
    int SeasonYear,
    MembershipRole MyRole,
    int CurrentWeek,
    SubmissionStatus? MyCurrentWeekStatus,
    LeagueLanding Landing,
    int DisplayWeek);
