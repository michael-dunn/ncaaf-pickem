using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Infrastructure.Events;

/// <summary>
/// Delivers domain events to their handlers, in process and synchronously, after the raising
/// service has saved (01-Architecture.md, "Domain events"). No message bus.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Delivers every event to every registered handler, in order, awaiting each one. A handler
    /// that throws is logged and skipped so one subscriber cannot stop the rest: scoring must not
    /// be blocked by a failing notification.
    /// </summary>
    /// <param name="domainEvents">The events to deliver, in the order they were raised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
