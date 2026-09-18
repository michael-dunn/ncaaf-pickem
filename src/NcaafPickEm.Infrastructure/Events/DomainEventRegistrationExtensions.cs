using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Infrastructure.Events;

/// <summary>Registration helpers for the in-process domain event plumbing.</summary>
public static class DomainEventRegistrationExtensions
{
    /// <summary>
    /// Registers the collector and the dispatcher. Called once by <c>AddInfrastructure</c>;
    /// feature phases add their handlers with
    /// <see cref="AddDomainEventHandler{TEvent, THandler}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<DomainEventCollector>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }

    /// <summary>
    /// Subscribes <typeparamref name="THandler"/> to <typeparamref name="TEvent"/>. Several
    /// handlers may subscribe to the same event; all of them run, in registration order.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <typeparam name="TEvent">The event to subscribe to.</typeparam>
    /// <typeparam name="THandler">The handler.</typeparam>
    public static IServiceCollection AddDomainEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        // Not TryAdd: two different handlers for one event are the normal case, and registering
        // the same one twice is a bug the caller should see as a double run rather than a silent
        // no-op.
        services.AddScoped<IDomainEventHandler<TEvent>, THandler>();
        return services;
    }
}
