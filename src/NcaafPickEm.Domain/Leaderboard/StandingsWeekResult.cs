namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>
/// One <c>WeekResults</c> row flattened for the calculator: what a member scored in one week of
/// one league.
/// </summary>
/// <param name="MembershipId">Whose result this is.</param>
/// <param name="Week">The league week the result belongs to.</param>
/// <param name="Points">Points earned that week.</param>
/// <param name="CorrectCount">Correct picks that week.</param>
/// <param name="ActiveGameCount">Active (non-voided) games in the set when the row was computed.</param>
/// <param name="IsWeekComplete">Copy of the set's complete flag at compute time. A week counts
/// towards <c>WeeklyWins</c>, and gets a snapshot, only once it is complete.</param>
public sealed record StandingsWeekResult(
    Guid MembershipId,
    int Week,
    int Points,
    int CorrectCount,
    int ActiveGameCount,
    bool IsWeekComplete);
