using CollegeFootballData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Cfbd;
using NcaafPickEm.Infrastructure.Providers.Espn;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Infrastructure.Services;

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

        string referenceDataProvider = configuration["Providers:ReferenceData"] ?? string.Empty;

        // Season calendar (P0-05). Cfbd registers a scoped source over the ingested SeasonWeeks
        // table before the fixture one below; TryAddSingleton then no-ops, since a service
        // descriptor for ISeasonWeekSource already exists.
        services.TryAddSingleton<SeasonCalendar>();
        if (string.Equals(referenceDataProvider, "Cfbd", StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddScoped<ISeasonWeekSource, DbSeasonWeekSource>();
        }

        services.TryAddSingleton<ISeasonWeekSource, FixtureSeasonWeekSource>();

        // Game-set and point-value configuration (P3-03). Registered before LeagueService,
        // which takes PointRuleService to re-resolve unlocked weeks when DefaultPointValue changes.
        services.AddScoped<PointRuleService>();
        services.AddScoped<GameSetService>();

        // Leagues and members (P1-01). Scoped: both take AppDbContext.
        services.AddScoped<LeagueService>();
        services.AddScoped<InviteService>();

        // Background jobs (P0-06). Registered after the migrator so the schema is in place before
        // the first tick. Later phases add their jobs with AddScheduledJob<T>() / AddOneShotJob<T>()
        // right here; see JobRegistrationExtensions and the "Jobs" section of AGENT-NOTES.md.
        services.AddJobScheduler(configuration);
        // Reference data and live scores (P2-02/P2-03/P2-05). Providers:ReferenceData and
        // Providers:LiveScores select the implementation; Fixture is the only one today and is
        // the default in Development when the key is unset. The snapshot state is always
        // registered (cheap, and the admin endpoint needs it even if a real provider is chosen
        // for reference data but Fixture for live scores).
        services.TryAddSingleton<FixtureSnapshotState>();

        // In-process domain events (P2-03). Collector + dispatcher only; each phase registers its
        // own handlers with services.AddDomainEventHandler<TEvent, THandler>() right here.
        services.AddDomainEvents();

        // Every outbound provider call is recorded in ProviderCalls (Features 09 and 12).
        services.TryAddSingleton<IProviderCallRecorder, ProviderCallRecorder>();

        // Applies a live-score snapshot to Games and raises GameWentFinal / GameScheduleChanged.
        services.TryAddScoped<LiveScoreApplyService>();

        RegisterReferenceDataProvider(services, configuration, referenceDataProvider, environment);

        // Provider-neutral: it ingests whatever IReferenceDataProvider is registered above, so it
        // must be resolvable under Fixture too (P2-04's refresh jobs and the admin refresh route
        // run in Development against the fixture provider).
        services.TryAddScoped<ReferenceDataIngestService>();

        string liveScoreProvider = configuration["Providers:LiveScores"] ?? string.Empty;
        RegisterLiveScoreProvider(services, liveScoreProvider, environment);

        services.TryAddScoped<FixtureSeeder>();
        services.AddHostedService<FixtureSeederHostedService>();

        return services;
    }

    private static void RegisterReferenceDataProvider(
        IServiceCollection services,
        IConfiguration configuration,
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

        // Case-insensitive, to agree with the ISeasonWeekSource branch in AddInfrastructure that
        // decides whether DbSeasonWeekSource is registered for this same setting.
        if (string.Equals(providerName, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddSingleton<IReferenceDataProvider, FixtureReferenceDataProvider>();
        }
        else if (string.Equals(providerName, "Cfbd", StringComparison.OrdinalIgnoreCase))
        {
            RegisterCfbdClient(services, configuration);
            services.TryAddSingleton<IReferenceDataProvider, CfbdReferenceDataProvider>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Providers:ReferenceData '{providerName}' is not a recognized provider. " +
                "Use 'Fixture' or 'Cfbd'.");
        }
    }

    private static void RegisterCfbdClient(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CfbdOptions>().Bind(configuration.GetSection(CfbdOptions.SectionName));
        services.AddHttpClient("Cfbd", client => client.BaseAddress = new Uri("https://api.collegefootballdata.com"));

        services.TryAddSingleton(sp =>
        {
            // CfbdAccessTokenProvider is built here rather than registered as IAccessTokenProvider:
            // that interface is a generic Kiota abstraction, and a second Kiota client added later
            // would otherwise resolve it and be handed CFBD's key.
            var authenticationProvider = new BaseBearerTokenAuthenticationProvider(
                new CfbdAccessTokenProvider(sp.GetRequiredService<IOptionsMonitor<CfbdOptions>>()));
            HttpClient httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Cfbd");
            var requestAdapter = new HttpClientRequestAdapter(authenticationProvider, httpClient: httpClient);
            return new ApiClient(requestAdapter);
        });
    }

    private static void RegisterLiveScoreProvider(
        IServiceCollection services,
        string providerName,
        IHostEnvironment environment)
    {
        LiveScoreSource configured = ParseLiveScoreSource(providerName, environment);

        // Singleton: which source is answering, and whether the fallback has engaged, is per
        // process and per game day (04-Domain-Algorithms.md section 10).
        services.TryAddSingleton<ILiveScoreHealth>(provider =>
            new LiveScoreHealth(configured, provider.GetRequiredService<ILogger<LiveScoreHealth>>()));

        if (configured == LiveScoreSource.Fixture)
        {
            services.TryAddSingleton<ILiveScoreProvider, FixtureLiveScoreProvider>();
            return;
        }

        services.AddHttpClient<EspnLiveScoreProvider>(client =>
        {
            client.BaseAddress = new Uri(EspnLiveScoreProvider.DefaultBaseAddress);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.TryAddScoped<CfbdLiveScoreProvider>();

        // Both real sources are always constructed; which one is called is a runtime decision the
        // composite delegates to ILiveScoreHealth, so the poller only knows one provider.
        services.TryAddScoped<ILiveScoreProvider>(provider => new CompositeLiveScoreProvider(
            provider.GetRequiredService<EspnLiveScoreProvider>(),
            provider.GetRequiredService<CfbdLiveScoreProvider>(),
            provider.GetRequiredService<ILiveScoreHealth>(),
            provider.GetRequiredService<ILogger<CompositeLiveScoreProvider>>()));
    }

    private static LiveScoreSource ParseLiveScoreSource(string providerName, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "Providers:LiveScores is not set. Set it to 'Fixture', 'Espn', or 'Cfbd' " +
                    $"explicitly outside Development (environment is '{environment.EnvironmentName}').");
            }

            return LiveScoreSource.Fixture;
        }

        return providerName switch
        {
            "Fixture" => LiveScoreSource.Fixture,
            "Espn" => LiveScoreSource.Espn,
            "Cfbd" => LiveScoreSource.Cfbd,
            _ => throw new InvalidOperationException(
                $"Providers:LiveScores '{providerName}' is not a recognized provider. " +
                "Use 'Fixture', 'Espn' or 'Cfbd'."),
        };
    }
}
