using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Infrastructure.Events;

/// <summary>
/// Holds the events an application service has raised but not yet dispatched. Scoped, so one
/// request or one job run collects into one instance.
/// </summary>
/// <remarks>
/// The order is always: mutate entities, <see cref="Raise"/> as you go, <c>SaveChangesAsync</c>,
/// then dispatch <see cref="TakeAll"/> through <see cref="IDomainEventDispatcher"/>. Raising
/// before the save is what makes "the event says something that is already true in the database"
/// hold for every subscriber; taking the events out of the collector is what stops a second
/// dispatch in the same scope re-delivering them.
/// </remarks>
public sealed class DomainEventCollector
{
    private readonly List<IDomainEvent> _events = [];

    /// <summary>Events raised in this scope and not yet taken, in the order they were raised.</summary>
    public IReadOnlyList<IDomainEvent> Pending => _events;

    /// <summary>Queues an event for dispatch after the next save.</summary>
    /// <param name="domainEvent">The event to raise.</param>
    public void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _events.Add(domainEvent);
    }

    /// <summary>Returns everything raised so far and empties the collector.</summary>
    public IReadOnlyList<IDomainEvent> TakeAll()
    {
        IDomainEvent[] taken = [.. _events];
        _events.Clear();
        return taken;
    }
}
