using NcaafPickEm.Shared.Contracts.Leaderboard;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the leaderboard routes in 03-API-Contracts.md ("Leaderboard", Feature 07).
/// </summary>
public interface ILeaderboardApi
{
    /// <summary><c>GET /api/leagues/{leagueId}/leaderboard</c>: season standings.</summary>
    Task<SeasonLeaderboard> GetSeasonLeaderboardAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}/weeks/{week}/leaderboard</c>: one week's standings.</summary>
    Task<WeekLeaderboard> GetWeekLeaderboardAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/leagues/{leagueId}/weeks/{week}/grid</c>: every member's picks for a week
    /// alongside the results. Throws <see cref="LeaguesApiException"/> with
    /// <c>StatusCode == 403</c> ("PicksNotVisible") before the week locks.
    /// </summary>
    Task<WeekGrid> GetWeekGridAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);
}
