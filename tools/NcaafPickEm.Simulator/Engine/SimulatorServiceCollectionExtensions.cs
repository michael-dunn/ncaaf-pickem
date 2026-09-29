using NcaafPickEm.Infrastructure;
using NcaafPickEm.Infrastructure.Providers;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>Composes the simulator on top of the app's own Infrastructure.</summary>
public static class SimulatorServiceCollectionExtensions
{
    /// <summary>
    /// The configuration the simulator always runs the app's services with, whatever the
    /// appsettings say: both providers are the simulator (so nothing ingests or polls behind its
    /// back), the app's scheduler never ticks in this process (the app runs the jobs; the simulator
    /// runs them only when asked), and the schema is migrated on start, which creates the
    /// simulator's database on first run.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> ForcedSettings = new Dictionary<string, string?>
    {
        ["Providers:ReferenceData"] = DependencyInjection.SimulatorProviderName,
        ["Providers:LiveScores"] = DependencyInjection.SimulatorProviderName,
        ["Jobs:Enabled"] = "false",
        ["Database:MigrateOnStartup"] = "true",
        ["Seed:DemoLeague"] = "false",
    };

    /// <summary>
    /// Registers the app's Infrastructure (with the simulator's scoreboard as its live-score
    /// provider) and the simulator engine: the desks, the world, the database guard and autoplay.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Configuration with <see cref="ForcedSettings"/> applied last.</param>
    /// <param name="environment">The host environment; must be Development or Testing.</param>
    /// <param name="runAutoplay">False in tests, which drive <see cref="SimWorld.AutoplayTickAsync"/> themselves.</param>
    public static IServiceCollection AddSimulator(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool runAutoplay = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Before AddInfrastructure: its Simulator branch TryAdds the app's always-empty provider,
        // which this registration pre-empts. Scoped, so a step's queue is its own.
        services.AddScoped<SimScoreFeed>();
        services.AddScoped<ILiveScoreProvider>(provider => provider.GetRequiredService<SimScoreFeed>());

        services.AddInfrastructure(configuration, environment);

        services.AddOptions<SimulatorOptions>().Bind(configuration.GetSection(SimulatorOptions.SectionName));

        services.AddScoped<SimStateStore>();
        services.AddScoped<CatalogDesk>();
        services.AddScoped<CalendarDesk>();
        services.AddScoped<SlateDesk>();
        services.AddScoped<ScoreDesk>();
        services.AddScoped<PeopleDesk>();
        services.AddScoped<WeekDesk>();
        services.AddScoped<JobDesk>();
        services.AddScoped<WipeDesk>();
        services.AddScoped<SnapshotReader>();

        services.AddSingleton<SimDatabaseStatus>();
        services.AddSingleton<SimActivity>();
        services.AddSingleton<SimWorld>();

        // After AddInfrastructure, so IHost starts it after DatabaseMigratorHostedService.
        services.AddHostedService<SimDatabaseGuard>();

        if (runAutoplay)
        {
            services.AddHostedService<GameDirector>();
        }

        return services;
    }
}
