namespace NcaafPickEm.Domain.Leaderboard;

/// <summary>
/// One row of the week grid: a game in the locked set, with "who won" already resolved by the
/// caller through <c>Domain/Scoring/WinnerResolver</c> (D-098), so the grid never derives it a
/// second way.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c> the cells key on.</param>
/// <param name="IsVoided">Voided after lock: every cell in the row reads
/// <c>GridOutcome.Voided</c>, whatever anybody picked.</param>
/// <param name="WinnerTeamId">The winning team, or null while the game has no determinable
/// winner - which is what makes a cell <c>Pending</c>.</param>
public sealed record GridGame(
    Guid GameSetGameId,
    bool IsVoided,
    Guid? WinnerTeamId);
