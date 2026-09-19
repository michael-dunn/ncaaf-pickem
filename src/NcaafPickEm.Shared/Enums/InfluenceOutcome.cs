namespace NcaafPickEm.Shared.Enums;

/// <summary>
/// How one game on the influence dashboard currently stands for the viewing member (Feature 05,
/// <c>04-Domain-Algorithms.md</c> section 6). Shared by the domain calculator and the
/// <c>DashboardGameDto</c> the endpoint returns, per D-014.
/// </summary>
public enum InfluenceOutcome : byte
{
    /// <summary>
    /// The viewer picked a team and the game has no determinable winner yet - not final, or final
    /// on a tie or with missing scores, which is the "needs review" path, not a loss.
    /// </summary>
    Pending = 0,

    /// <summary>The game has a winner and it is the team the viewer picked.</summary>
    Won = 1,

    /// <summary>The game has a winner and it is not the team the viewer picked.</summary>
    Lost = 2,

    /// <summary>The viewer has no pick on this game. Never becomes Won or Lost.</summary>
    NoPick = 3,
}
