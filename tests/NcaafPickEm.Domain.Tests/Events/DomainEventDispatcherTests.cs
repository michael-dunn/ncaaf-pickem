using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Domain.Events;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Events;

/// <summary>
/// The in-process domain event plumbing (P2-03): the collector queues, the dispatcher delivers to
/// every registered handler for the event's own type, and a handler that throws is logged and
/// stepped over so one subscriber cannot block scoring.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private static readonly GameWentFinal WentFinal =
        new(Guid.CreateVersion7(), 2026, 7, Guid.CreateVersion7()) { OccurredUtc = new DateTime(2026, 10, 18, 3, 30, 0, DateTimeKind.Utc) };

    [Fact]
    public async Task GivenTwoHandlers_WhenAnEventIsDispatched_ThenBothRun()
    {
        var first = new Recorder();
        var second = new Recorder();
        ServiceProvider services = Build(first, second);

        await Dispatcher(services).DispatchAsync([WentFinal]);

        first.Handled.Should().ContainSingle().Which.Should().Be(WentFinal);
        second.Handled.Should().ContainSingle();
    }

    [Fact]
    public async Task GivenAHandlerThatThrows_WhenAnEventIsDispatched_ThenTheOthersStillRun()
    {
        var before = new Recorder();
        var after = new Recorder();
        ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDomainEventHandler<GameWentFinal>>(before)
            .AddSingleton<IDomainEventHandler<GameWentFinal>>(new ThrowingHandler())
            .AddSingleton<IDomainEventHandler<GameWentFinal>>(after)
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>()
            .BuildServiceProvider();

        await Dispatcher(services).DispatchAsync([WentFinal]);

        before.Handled.Should().ContainSingle();
        after.Handled.Should().ContainSingle();
    }

    [Fact]
    public async Task GivenHandlersForOneEventType_WhenAnotherTypeIsDispatched_ThenTheyAreNotCalled()
    {
        var recorder = new Recorder();
        ServiceProvider services = Build(recorder);

        var scheduleChanged = new GameScheduleChanged(Guid.CreateVersion7(), GameStatus.Scheduled, GameStatus.Postponed)
        {
            OccurredUtc = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc),
        };

        await Dispatcher(services).DispatchAsync([scheduleChanged]);

        recorder.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenNoHandlerAtAll_WhenAnEventIsDispatched_ThenNothingThrows()
    {
        ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>()
            .BuildServiceProvider();

        await Dispatcher(services).Invoking(d => d.DispatchAsync([WentFinal])).Should().NotThrowAsync();
    }

    [Fact]
    public void GivenTheCollector_WhenEventsAreTaken_ThenTheyComeBackOnceInOrder()
    {
        var collector = new DomainEventCollector();
        var scheduleChanged = new GameScheduleChanged(Guid.CreateVersion7(), GameStatus.Scheduled, GameStatus.Cancelled);

        collector.Raise(WentFinal);
        collector.Raise(scheduleChanged);
        collector.Pending.Should().HaveCount(2);

        IReadOnlyList<IDomainEvent> taken = collector.TakeAll();

        taken.Should().ContainInOrder(WentFinal, scheduleChanged);
        collector.Pending.Should().BeEmpty();
        collector.TakeAll().Should().BeEmpty();
    }

    private static ServiceProvider Build(params Recorder[] handlers)
    {
        IServiceCollection services = new ServiceCollection().AddLogging();
        foreach (Recorder handler in handlers)
        {
            services.AddSingleton<IDomainEventHandler<GameWentFinal>>(handler);
        }

        return services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>().BuildServiceProvider();
    }

    private static IDomainEventDispatcher Dispatcher(ServiceProvider services) =>
        services.CreateScope().ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

    private sealed class Recorder : IDomainEventHandler<GameWentFinal>
    {
        public List<GameWentFinal> Handled { get; } = [];

        public Task HandleAsync(GameWentFinal domainEvent, CancellationToken cancellationToken)
        {
            Handled.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<GameWentFinal>
    {
        public Task HandleAsync(GameWentFinal domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("this subscriber is broken");
    }
}
