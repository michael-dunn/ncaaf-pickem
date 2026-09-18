using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.Seasons.Events;

/// <summary>
/// A game reached <c>Final</c> for the first time. Raised exactly once per game by the live score
/// apply service; scoring (Feature 06) and notifications (Feature 11) subscribe.
/// </summary>
/// <remarks>
/// <c>Week</c> is the provider's week, copied from the <c>Games</c> row, so a game that kicks on
/// Saturday and finishes after midnight Eastern still scores against the week it was scheduled in
/// (Feature 06, "delayed games").
/// </remarks>
/// <param name="GameId">The game that went final.</param>
/// <param name="SeasonYear">Season of the game.</param>
/// <param name="Week">Provider week of the game, not the league's week number.</param>
/// <param name="WinnerTeamId">
/// The team with the higher score, or <see langword="null"/> when the feed reported a tie or no
/// scores. A null winner means "needs review": nobody is awarded points until a commissioner
/// overrides or voids the game (04-Domain-Algorithms.md section 7).
/// </param>
public sealed record GameWentFinal(
    Guid GameId,
    int SeasonYear,
    int Week,
    Guid? WinnerTeamId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
