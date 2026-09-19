using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.GameSets.Events;

/// <summary>
/// Raised once per week, by the lock job, after the week's spreads and point values have been
/// frozen and every active member's <c>WeekSubmissions</c> row has been settled to
/// <c>Locked</c> or <c>Incomplete</c> (Features 04 and 05,
/// <c>04-Domain-Algorithms.md</c> section 5).
/// </summary>
/// <remarks>
/// Dispatched after <c>SaveChangesAsync</c>, so a subscriber reads a database that already agrees
/// with the event: the set is locked, every snapshot is written, and every picks and
/// configuration endpoint for the week already refuses. Expected subscribers are the notification
/// phase (P7) and the dashboard (P8); scoring subscribes to <c>GameWentFinal</c> instead.
/// </remarks>
/// <param name="LeagueId">The league whose week locked.</param>
/// <param name="Week">The week number.</param>
/// <param name="WeekGameSetId">The <c>WeekGameSets.Id</c> that locked.</param>
/// <param name="LockedUtc">
/// What was written to <c>WeekGameSets.LockedUtc</c>: when the job actually ran, which is at or
/// after <c>LockAtUtc</c> (section 5 step 3 keeps a late run visible).
/// </param>
public sealed record WeekLocked(
    Guid LeagueId,
    int Week,
    Guid WeekGameSetId,
    DateTime LockedUtc) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
