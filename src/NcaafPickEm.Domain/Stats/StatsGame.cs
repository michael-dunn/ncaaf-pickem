namespace NcaafPickEm.Domain.Stats;

/// <summary>
/// One active (neither removed nor voided) row of a locked week's set, flattened for
/// <see cref="MemberStatsCalculator"/>. "Who won" is the caller's answer from
/// <c>Domain/Scoring/WinnerResolver</c> (D-098); the calculator never derives it a second way.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c> picks key on.</param>
/// <param name="Week">The league week the set belongs to.</param>
/// <param name="HomeTeamId">Home team.</param>
/// <param name="AwayTeamId">Away team.</param>
/// <param name="SpreadAtLock">The line frozen at lock, home minus away (negative = home favored),
/// or null when the game had none.</param>
/// <param name="WinnerTeamId">The winner, or null while the game has no final result. Only a game
/// with a winner is a counted game.</param>
/// <param name="IsWeekComplete">The set's complete flag; a counted game in a week that is not
/// complete makes the stats provisional.</param>
public sealed record StatsGame(
    Guid GameSetGameId,
    int Week,
    Guid HomeTeamId,
    Guid AwayTeamId,
    decimal? SpreadAtLock,
    Guid? WinnerTeamId,
    bool IsWeekComplete)
{
    /// <summary>
    /// The team getting points per <see cref="SpreadAtLock"/>, or null when there was no line or
    /// it was exactly 0 (Feature 14, "Underdog").
    /// </summary>
    public Guid? UnderdogTeamId => SpreadAtLock switch
    {
        null => null,
        < 0m => AwayTeamId,
        > 0m => HomeTeamId,
        _ => null,
    };
}
