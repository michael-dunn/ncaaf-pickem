using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.GameSets.Events;

/// <summary>
/// Raised when one or more games are added to a league's week (generation or a manual add,
/// Feature 02).
/// </summary>
/// <param name="LeagueId">The league the week belongs to.</param>
/// <param name="Week">The week number.</param>
/// <param name="WeekGameSetId">The <c>WeekGameSets.Id</c> the games were added to.</param>
/// <param name="GameSetGameIds">The <c>WeekGameSetGames.Id</c> rows that were added.</param>
/// <param name="Reason">Why: "Generated", "Rule regeneration", "Manual", or "Schedule change"
/// (P3-04: a postponed/cancelled game returned to <c>Scheduled</c> before lock).</param>
public sealed record GameAddedToSet(
    Guid LeagueId,
    int Week,
    Guid WeekGameSetId,
    IReadOnlyList<Guid> GameSetGameIds,
    string Reason) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
