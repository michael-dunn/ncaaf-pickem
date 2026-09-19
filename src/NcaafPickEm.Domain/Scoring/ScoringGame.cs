using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Scoring;

/// <summary>
/// One row of a locked week's game set as <see cref="WeekScorer"/> sees it: enough to decide
/// whether it still counts, who won it, and what it is worth.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c>. Picks are keyed on this, not on the game.</param>
/// <param name="HomeTeamId">Home team of the underlying game.</param>
/// <param name="AwayTeamId">Away team of the underlying game.</param>
/// <param name="PointValue">
/// <c>WeekGameSetGames.ResolvedPointValue</c>, frozen at lock (Feature 03). Whatever the point
/// rules say today, a locked week is scored at the value picks were made against.
/// </param>
/// <param name="Status">The underlying game's current status.</param>
/// <param name="HomeScore">Home points, or null when the provider has not reported them.</param>
/// <param name="AwayScore">Away points, or null when the provider has not reported them.</param>
/// <param name="ResultOverrideWinnerTeamId">The commissioner's correction, or null.</param>
/// <param name="IsVoided">
/// Voided after lock (Feature 06). A voided game scores for nobody, counts against nobody's
/// <see cref="MemberWeekScore.ActiveGameCount"/>, and does not hold the week open.
/// </param>
/// <param name="IsRemoved">
/// Removed before lock (D-008). Rows are handed over whole; the scorer filters them out itself.
/// </param>
public readonly record struct ScoringGame(
    Guid GameSetGameId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    int PointValue,
    GameStatus Status,
    int? HomeScore,
    int? AwayScore,
    Guid? ResultOverrideWinnerTeamId,
    bool IsVoided,
    bool IsRemoved)
{
    /// <summary>A game counts for scoring only while it is neither removed nor voided.</summary>
    public bool IsActive => !IsRemoved && !IsVoided;
}
