using NcaafPickEm.Domain.Events;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Seasons.Events;

/// <summary>
/// A game moved into, or back out of, a status that stops it being played as scheduled:
/// <see cref="GameStatus.Postponed"/> or <see cref="GameStatus.Cancelled"/>. Game set maintenance
/// (Feature 02, P3-04) subscribes so the game leaves or rejoins the affected weeks' sets.
/// </summary>
/// <param name="GameId">The game whose status changed.</param>
/// <param name="OldStatus">Status before the change.</param>
/// <param name="NewStatus">Status after the change.</param>
public sealed record GameScheduleChanged(
    Guid GameId,
    GameStatus OldStatus,
    GameStatus NewStatus) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
