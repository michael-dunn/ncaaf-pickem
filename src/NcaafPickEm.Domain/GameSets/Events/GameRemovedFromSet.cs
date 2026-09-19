using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.GameSets.Events;

/// <summary>
/// Raised when one game is taken out of a league's week, before lock (Feature 02).
/// </summary>
/// <param name="LeagueId">The league the week belongs to.</param>
/// <param name="Week">The week number.</param>
/// <param name="WeekGameSetId">The <c>WeekGameSets.Id</c> the game was removed from.</param>
/// <param name="GameSetGameId">The <c>WeekGameSetGames.Id</c> row that was flagged removed.</param>
/// <param name="GameId">The underlying <c>Games.Id</c>.</param>
/// <param name="Reason">Why: "Rule regeneration", "Schedule change", or "Manual".</param>
public sealed record GameRemovedFromSet(
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
