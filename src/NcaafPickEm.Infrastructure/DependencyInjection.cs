using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Fixture;

namespace NcaafPickEm.Infrastructure;

/// <summary>
/// Single registration point for everything in the Infrastructure project.
/// Program.cs calls this once; later phases add registrations here, not in Program.cs.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, providers, push, and background jobs.
    /// </summary>
    /// <remarks>
    /// Intentionally empty in P0-01. Later phases add, in this order:
    /// <list type="bullet">
    ///   <item>P0-02 — <c>AppDbContext</c> against <c>ConnectionStrings:Default</c>, plus repositories.</item>
    ///   <item>P0-06 — <c>JobScheduler</c> hosted service, <c>IOneShotScheduler</c>, <c>HeartbeatJob</c>,
    ///         gated on <c>Jobs:Enabled</c>.</item>
    ///   <item>P2-02/P2-03/P2-05 — <c>IReferenceDataProvider</c> and <c>ILiveScoreProvider</c> selected by
    ///         <c>Providers:ReferenceData</c> and <c>Providers:LiveScores</c> (Cfbd | Espn | Fixture).</item>
    ///   <item>P5-01 — application services and the <c>IDomainEventDispatcher</c>.</item>
    ///   <item>P7-01 — web push sender and VAPID options from <c>Push:*</c>.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // TimeProvider is the only clock the codebase may use (05-Conventions.md).
        // TryAdd so tests can register a FakeTimeProvider before calling this.
        services.TryAddSingleton(TimeProvider.System);

        // Season calendar (P0-05). The week source is fixture-backed for every provider setting
        // today; P2-02 adds a CFBD-ingest-backed source and selects it when
        // Providers:ReferenceData is Cfbd.
        services.TryAddSingleton<SeasonCalendar>();
        services.TryAddSingleton<ISeasonWeekSource, FixtureSeasonWeekSource>();

        return services;
    }
}
