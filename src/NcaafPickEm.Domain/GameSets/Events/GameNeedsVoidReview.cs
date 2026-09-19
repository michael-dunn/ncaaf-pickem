using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.GameSets.Events;

/// <summary>
/// Raised when a game inside an already-locked week is postponed or cancelled (Feature 02,
/// P3-04). Nothing here removes or voids the row - only the commissioner corrections flow
/// (Feature 06, P5-02) may do that - this event exists so the data page (P2-04) can list the game
/// as needing a decision.
/// </summary>
/// <param name="LeagueId">The league the week belongs to.</param>
/// <param name="Week">The week number.</param>
/// <param name="WeekGameSetId">The <c>WeekGameSets.Id</c> the game belongs to.</param>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c> row that needs a decision.</param>
/// <param name="GameId">The underlying <c>Games.Id</c>.</param>
/// <param name="Reason">Why: currently always "Schedule change".</param>
public sealed record GameNeedsVoidReview(
    Guid LeagueId,
    int Week,
    Guid WeekGameSetId,
    Guid GameSetGameId,
    Guid GameId,
    string Reason) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
