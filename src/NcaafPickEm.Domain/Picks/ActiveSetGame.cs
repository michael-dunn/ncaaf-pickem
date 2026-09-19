namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// One active game of a week's set, as <see cref="SubmissionStatusCalculator"/> sees it: an id to
/// match picks against and the instant the game joined the set.
/// </summary>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c>.</param>
/// <param name="AddedUtc">
/// When the row was inserted, or re-activated after a removal — the instant the week's
/// <c>TotalCount</c> last increased because of this game (04-Domain-Algorithms.md section 4).
/// </param>
public readonly record struct ActiveSetGame(Guid GameSetGameId, DateTime AddedUtc);
