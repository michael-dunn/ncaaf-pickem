using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Turns the server's <see cref="LeagueSummary.Landing"/> into the route the app opens that
/// league on (04-Domain-Algorithms.md section 12). The routes are the bottom tab bar's.
/// </summary>
public static class LeagueLandingRoute
{
    /// <summary>The route for a league's landing screen.</summary>
    /// <param name="league">A league the member belongs to.</param>
    public static string For(LeagueSummary league)
    {
        ArgumentNullException.ThrowIfNull(league);

        string home = $"/leagues/{league.LeagueId}";
        return league.Landing switch
        {
            LeagueLanding.Picks => $"{home}/weeks/{league.CurrentWeek}/picks",
            LeagueLanding.Dashboard => $"{home}/weeks/{league.CurrentWeek}/dashboard",
            LeagueLanding.Leaderboard => $"{home}/leaderboard",
            _ => home,
        };
    }
}
