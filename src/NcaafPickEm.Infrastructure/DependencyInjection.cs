using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Seeding;

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
    /// Later phases add, in this order:
    /// <list type="bullet">
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
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // TimeProvider is the only clock the codebase may use (05-Conventions.md).
        // TryAdd so tests can register a FakeTimeProvider before calling this.
        services.TryAddSingleton(TimeProvider.System);

        // An unset connection string has to reach UseSqlServer as null, not "": empty throws at
        // registration, whereas null lets the app boot and /health/ready report the problem.
        string? connectionString = configuration.GetConnectionString(DatabaseDefaults.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = null;
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddHostedService<DatabaseMigratorHostedService>();

        // Season calendar (P0-05). The week source is fixture-backed for every provider setting
        // today; P2-02 adds a CFBD-ingest-backed source and selects it when
        // Providers:ReferenceData is Cfbd.
        services.TryAddSingleton<SeasonCalendar>();
        services.TryAddSingleton<ISeasonWeekSource, FixtureSeasonWeekSource>();

        // Reference data and live scores (P2-02/P2-03/P2-05). Providers:ReferenceData and
        // Providers:LiveScores select the implementation; Fixture is the only one today and is
        // the default in Development when the key is unset. The snapshot state is always
        // registered (cheap, and the admin endpoint needs it even if a real provider is chosen
        // for reference data but Fixture for live scores).
        services.TryAddSingleton<FixtureSnapshotState>();

        string referenceDataProvider = configuration["Providers:ReferenceData"] ?? string.Empty;
        RegisterReferenceDataProvider(services, referenceDataProvider, environment);

        string liveScoreProvider = configuration["Providers:LiveScores"] ?? string.Empty;
        RegisterLiveScoreProvider(services, liveScoreProvider, environment);

        services.TryAddScoped<FixtureSeeder>();
        services.AddHostedService<FixtureSeederHostedService>();

        return services;
    }

    private static void RegisterReferenceDataProvider(
        IServiceCollection services,
        string providerName,
        IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            // Missing config = Fixture in Development (and Testing, which behaves like
            // Development for provider purposes); anywhere else it is a misconfiguration, since a
            // real provider needs an API key that is never assumed to be present.
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Providers:ReferenceData is not set. Set it to 'Fixture' or 'Cfbd' explicitly " +
                    $"outside Development (environment is '{environment.EnvironmentName}').");
            }

            services.TryAddSingleton<IReferenceDataProvider, FixtureReferenceDataProvider>();
            return;
        }

        switch (providerName)
        {
            case "Fixture":
                services.TryAddSingleton<IReferenceDataProvider, FixtureReferenceDataProvider>();
                break;

            // P2-02 registers Cfbd here:
            // case "Cfbd":
            //     services.TryAddSingleton<IReferenceDataProvider, CfbdReferenceDataProvider>();
            //     break;

            default:
                throw new InvalidOperationException(
                    $"Providers:ReferenceData '{providerName}' is not a recognized provider. " +
                    "Use 'Fixture' (or 'Cfbd' once P2-02 registers it).");
        }
    }

    private static void RegisterLiveScoreProvider(
        IServiceCollection services,
        string providerName,
        IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Providers:LiveScores is not set. Set it to 'Fixture', 'Espn', or 'Cfbd' " +
                    $"explicitly outside Development (environment is '{environment.EnvironmentName}').");
            }

            services.TryAddSingleton<ILiveScoreProvider, FixtureLiveScoreProvider>();
            return;
        }

        switch (providerName)
        {
            case "Fixture":
                services.TryAddSingleton<ILiveScoreProvider, FixtureLiveScoreProvider>();
                break;

            // P2-03 registers Espn/Cfbd here:
            // case "Espn":
            //     services.TryAddSingleton<ILiveScoreProvider, EspnLiveScoreProvider>();
            //     break;
            // case "Cfbd":
            //     services.TryAddSingleton<ILiveScoreProvider, CfbdLiveScoreProvider>();
            //     break;

            default:
                throw new InvalidOperationException(
                    $"Providers:LiveScores '{providerName}' is not a recognized provider. " +
                    "Use 'Fixture' (or 'Espn'/'Cfbd' once P2-03 registers them).");
        }
    }
}
