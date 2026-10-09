using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.Seasons.Events;

/// <summary>
/// A game's newest betting line changed: a line with a different spread was appended, one
/// appeared for a game that had none, or (the simulator's "No line") every line was taken away.
/// Point values subscribe (P3-06, D-189) so a close-spread rule keeps matching the line members
/// can see until the week locks.
/// </summary>
/// <param name="GameId">The game whose line changed.</param>
public sealed record GameLineChanged(Guid GameId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
