using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Infrastructure.Events;

/// <summary>
/// The only <see cref="IDomainEventDispatcher"/>. Resolves <see cref="IDomainEventHandler{TEvent}"/>
/// implementations for each event's runtime type out of the scope it was constructed in, and
/// awaits them one at a time.
/// </summary>
/// <remarks>
/// Scoped, not singleton: handlers are scoped services that usually need the same
/// <c>AppDbContext</c> as the service that raised the event.
/// </remarks>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> Invokers = new();

    private readonly IServiceProvider _services;
    private readonly ILogger<DomainEventDispatcher> _logger;

    /// <summary>Creates the dispatcher.</summary>
    /// <param name="services">The scope handlers are resolved from.</param>
    /// <param name="logger">Logger.</param>
    public DomainEventDispatcher(IServiceProvider services, ILogger<DomainEventDispatcher> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            Type eventType = domainEvent.GetType();
            HandlerInvoker invoker = Invokers.GetOrAdd(eventType, CreateInvoker);

            object[] handlers = [.. _services.GetServices(invoker.HandlerType).OfType<object>()];
            if (handlers.Length == 0)
            {
                _logger.LogDebug("No handler subscribed to {DomainEvent}", eventType.Name);
                continue;
            }

            foreach (object handler in handlers)
            {
                try
                {
                    await invoker.InvokeAsync(handler, domainEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One subscriber must never stop the others; scoring cannot wait on push.
                    _logger.LogError(
                        ex,
                        "Handler {Handler} failed for {DomainEvent}; continuing with the remaining handlers",
                        handler.GetType().Name,
                        eventType.Name);
                }
            }
        }
    }

    private static HandlerInvoker CreateInvoker(Type eventType)
    {
        Type invokerType = typeof(HandlerInvoker<>).MakeGenericType(eventType);
        return (HandlerInvoker)Activator.CreateInstance(invokerType)!;
    }

    private abstract class HandlerInvoker
    {
        public abstract Type HandlerType { get; }

        public abstract Task InvokeAsync(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken);
    }

    private sealed class HandlerInvoker<TEvent> : HandlerInvoker
        where TEvent : IDomainEvent
    {
        public override Type HandlerType => typeof(IDomainEventHandler<TEvent>);

        public override Task InvokeAsync(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken) =>
            ((IDomainEventHandler<TEvent>)handler).HandleAsync((TEvent)domainEvent, cancellationToken);
    }
}
