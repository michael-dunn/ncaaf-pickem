namespace NcaafPickEm.Domain.Dashboard;

/// <summary>
/// One member's whole influence dashboard for one locked week. Nothing here is persisted: the
/// endpoint projects it onto its DTOs and the UI renders it in the order it arrives.
/// </summary>
/// <param name="Games">
/// The games that matter, ordered by <c>OppositeCount</c> descending, then point value descending,
/// then kickoff ascending, then game-set-game id ascending so two runs over the same inputs produce
/// the same list. A game the viewer did not pick stays here however much the league agrees, because
/// it still has both teams' pickers to show.
/// </param>
/// <param name="EveryoneAgrees">
/// The games where every other member picked the same team the viewer did, in the same secondary
/// order. The UI collapses this section.
/// </param>
/// <param name="PointsSoFar">Sum of the point values of the games the viewer has already won.</param>
/// <param name="MaxRemaining">Sum of the point values still in play for the viewer - games they
/// picked that have not been decided yet.</param>
public sealed record InfluenceResult(
    IReadOnlyList<InfluenceGameResult> Games,
    IReadOnlyList<InfluenceGameResult> EveryoneAgrees,
    int PointsSoFar,
    int MaxRemaining);
