using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Dashboard;

/// <summary>One game on the viewer's influence dashboard (Feature 05, 04-Domain-Algorithms section 6).</summary>
/// <param name="Game">The game as every other endpoint renders it, including live score and winner.</param>
/// <param name="MyTeamId">The viewer's pick, or null.</param>
/// <param name="MyOutcome">Pending until a winner is determinable; Won/Lost afterwards; NoPick when the viewer did not pick.</param>
/// <param name="OppositeCount">Members who picked the team the viewer did not.</param>
/// <param name="OppositePicks">Those members, in roster order.</param>
/// <param name="NoPick">Members (not the viewer) with no pick on this game.</param>
/// <param name="HomePickers">Members who picked the home team; used when the viewer has no pick.</param>
/// <param name="AwayPickers">Members who picked the away team; used when the viewer has no pick.</param>
/// <param name="SwingPoints">PointValue times OppositeCount, informational.</param>
public sealed record DashboardGameDto(
    GameSetGameDto Game,
    Guid? MyTeamId,
    InfluenceOutcome MyOutcome,
    int OppositeCount,
    MemberRef[] OppositePicks,
    MemberRef[] NoPick,
    MemberRef[] HomePickers,
    MemberRef[] AwayPickers,
    int SwingPoints);
