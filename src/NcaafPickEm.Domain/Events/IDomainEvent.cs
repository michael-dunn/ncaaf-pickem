namespace NcaafPickEm.Domain.Events;

/// <summary>
/// Something that happened in the domain and that other features may react to, dispatched
/// in-process and synchronously after <c>SaveChangesAsync</c> (01-Architecture.md, "Domain
/// events"). Implementations are immutable records living in the feature folder that owns them,
/// for example <c>Domain/Seasons/Events</c>.
/// </summary>
/// <remarks>
/// <see cref="OccurredUtc"/> is an <c>init</c> property rather than a constructor parameter so
/// every event keeps the short positional signature its consumers read, while the raising
/// application service stamps the time from the injected <see cref="TimeProvider"/>: the Domain
/// project may never read a clock itself (05-Conventions.md).
/// </remarks>
public interface IDomainEvent
{
    /// <summary>When the event happened, in UTC, stamped by the service that raised it.</summary>
    DateTime OccurredUtc { get; init; }
}
