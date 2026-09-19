using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// One game as the viewing member sees it on the influence dashboard. Every member list excludes
/// the viewer, and lists the rest in the order <see cref="InfluenceRequest.MembersActiveAtLock"/>
/// gave them.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames</c> row this describes. The caller joins its
/// own <c>GameSetGameDto</c> on this.</param>
/// <param name="MyTeamId">The team the viewer picked, or null when they have no pick on it.</param>
/// <param name="MyOutcome">Pending, Won, Lost, or NoPick.</param>
/// <param name="OppositeCount">
/// <c>OppositePicks.Count</c>, the primary ordering key - how much of the league the viewer is
/// playing against on this one game. Zero when the viewer has no pick: with no team of their own
/// there is nobody to be opposite to.
/// </param>
/// <param name="OppositePicks">
/// Other members who picked the team the viewer did not. Empty when the viewer has no pick; the UI
/// shows <paramref name="HomePickers"/> and <paramref name="AwayPickers"/> instead.
/// </param>
/// <param name="NoPick">Other members with no pick on this game - the story's separate "No Pick"
/// group, never folded into <paramref name="OppositePicks"/>.</param>
/// <param name="HomePickers">Other members who picked the home team. Always filled, whether or not
/// the viewer has a pick.</param>
/// <param name="AwayPickers">Other members who picked the away team.</param>
/// <param name="SwingPoints"><c>PointValue * OppositeCount</c>. Informational: the points that
/// change hands between the viewer and the other side if this game goes one way or the other.</param>
/// <param name="WinnerTeamId">What <see cref="Scoring.WinnerResolver"/> made of the game: the
/// override, else the higher score once Final, else null.</param>
public sealed record InfluenceGameResult(
    Guid GameSetGameId,
    Guid? MyTeamId,
    InfluenceOutcome MyOutcome,
    int OppositeCount,
    IReadOnlyList<InfluenceMember> OppositePicks,
    IReadOnlyList<InfluenceMember> NoPick,
    IReadOnlyList<InfluenceMember> HomePickers,
    IReadOnlyList<InfluenceMember> AwayPickers,
    int SwingPoints,
    Guid? WinnerTeamId);
