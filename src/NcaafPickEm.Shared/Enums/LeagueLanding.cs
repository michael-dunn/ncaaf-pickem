namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// The screen the app opens a league on: whichever one the week's cycle makes the obvious first
/// look (04-Domain-Algorithms.md section 12, D-187).
/// </summary>
public enum LeagueLanding : byte
{
    /// <summary>
    /// The league home. The calendar has not reached the league's first week yet, so there is
    /// nothing to pick; the home page shows "Season starts Week N on &lt;date&gt;".
    /// </summary>
    LeagueHome = 0,

    /// <summary>The current week has not locked.</summary>
    Picks = 1,

    /// <summary>The current week has locked and its games are still being played.</summary>
    Dashboard = 2,

    /// <summary>
    /// The season leaderboard: the current week's games are all done, it is the Sunday after a
    /// played week, or the league's season is over.
    /// </summary>
    Leaderboard = 3,
}
