using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Infrastructure.Events;

/// <summary>
/// Reacts to one kind of domain event. Register with
/// <see cref="DomainEventRegistrationExtensions.AddDomainEventHandler{TEvent, THandler}"/>; the
/// dispatcher resolves every handler for an event from the scope that raised it.
/// </summary>
/// <remarks>
/// A handler must be safe to run more than once for the same event: the raising service saves
/// first and dispatches second, so a crash between the two means the event is lost, and a
/// nightly recompute (Feature 06) rather than the event is the safety net.
/// </remarks>
/// <typeparam name="TEvent">The event this handler reacts to.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles one event.</summary>
    /// <param name="domainEvent">The event that was raised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
