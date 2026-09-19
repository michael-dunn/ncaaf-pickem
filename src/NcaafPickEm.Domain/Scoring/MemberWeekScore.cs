namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// One member's score for the week, as <see cref="WeekScorer"/> computed it. The application
/// service upserts this onto the <c>WeekResults</c> row.
/// </summary>
/// <param name="MembershipId">Whose score it is.</param>
/// <param name="Points">
/// Sum of <see cref="ScoringGame.PointValue"/> over the active games whose winner this member
/// picked. Games still waiting on a result, and Final games with no determinable winner, add
/// nothing.
/// </param>
/// <param name="CorrectCount">How many of those games there were.</param>
/// <param name="ActiveGameCount">
/// Active games in the set - the same number for everybody in the week. Voided and removed rows
/// are excluded, so "4 of 9" shrinks to "4 of 8" the moment a game is voided.
/// </param>
public readonly record struct MemberWeekScore(
    Guid MembershipId,
    int Points,
    int CorrectCount,
    int ActiveGameCount);
