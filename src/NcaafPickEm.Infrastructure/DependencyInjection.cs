using System.Globalization;
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
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Scoring.Events;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Jobs.Refresh;
using NcaafPickEm.Infrastructure.Notifications;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Cfbd;
using NcaafPickEm.Infrastructure.Providers.Espn;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Infrastructure.Scoring;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Infrastructure.Time;

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
        // TryAdd so tests (and the simulate command) can register their own clock before calling
        // this. Development and Testing get the movable DevTimeProvider (P10-01, D-180), optionally
        // pre-shifted by Clock:NowUtc / Clock:Frozen; Production always reads real time.
        if (IsDevelopmentOrTesting(environment))
        {
            services.TryAddSingleton(sp => CreateDevClock(sp, configuration));
            services.TryAddSingleton<TimeProvider>(sp => sp.GetRequiredService<DevTimeProvider>());

            // Drives the demo league through a week by hand (generate, fill picks, lock, poll,
            // reset) behind /api/admin/fixture/demo. Development and Testing only, like the routes.
            services.TryAddScoped<DemoWeekService>();
        }
        else
        {
            services.TryAddSingleton(TimeProvider.System);
        }

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

        // P8-05: the migrator's "still working" flag, which /health/ready reads so a container
        // mid-migration answers 503 instead of "ready".
        services.TryAddSingleton<DatabaseStartupState>();
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

        // Weekly picks (P4-01). Scoped; takes GameSetService for the shared game projection.
        services.AddScoped<PickService>();

        // Influence dashboard (P6-02). Scoped; takes GameSetService for the same game projection
        // and the singleton ILiveScoreHealth for the "scores may be stale" flag.
        services.AddScoped<DashboardService>();

        // Leagues and members (P1-01). Scoped: both take AppDbContext.
        services.AddScoped<LeagueService>();
        services.AddScoped<InviteService>();

        // Leaderboards (P5-03). The snapshot writer must be registered before P5-01's
        // TryAddScoped of the no-op one, so the real implementation wins.
        services.AddScoped<IStandingsSnapshotWriter, StandingsSnapshotWriter>();
        services.AddScoped<LeaderboardService>();

        // Background jobs (P0-06). Registered after the migrator so the schema is in place before
        // the first tick. Later phases add their jobs with AddScheduledJob<T>() / AddOneShotJob<T>()
        // right here; see JobRegistrationExtensions and the "Jobs" section of AGENT-NOTES.md.
        services.AddJobScheduler(configuration);
        // P3-04: Tuesday auto-regeneration (after the P2-04 refresh jobs) and the Sunday
        // auto-create sweep, both against GameSetService.
        services.AddScheduledJob<RegenerateGameSetsJob>();
        services.AddScheduledJob<EnsureCurrentWeekSetsJob>();
        // P4-02: the per-week lock. A one-shot, so every tick simply asks which weeks are due.
        services.AddOneShotJob<LockWeekJob>();
        // Reference data and live scores (P2-02/P2-03/P2-05). Providers:ReferenceData and
        // Providers:LiveScores select the implementation; Fixture is the only one today and is
        // the default in Development when the key is unset. The snapshot state is always
        // registered (cheap, and the admin endpoint needs it even if a real provider is chosen
        // for reference data but Fixture for live scores).
        services.TryAddSingleton<FixtureSnapshotState>();

        // In-process domain events (P2-03). Collector + dispatcher only; each phase registers its
        // own handlers with services.AddDomainEventHandler<TEvent, THandler>() right here.
        services.AddDomainEvents();

        // Web push (P7-01). Binds Push:*, picks WebPushSender or NullPushSender from whether the
        // VAPID pair validates, and adds the PushRetry one-shot job. Missing keys are not a
        // startup failure; see PushRegistrationExtensions.
        services.AddPush(configuration);
        // P3-04: keeps WeekGameSetGames in sync with a game entering/leaving Postponed/Cancelled.
        services.AddDomainEventHandler<GameScheduleChanged, ScheduleChangeHandler>();
        // P4-04: recomputes WeekSubmissions status and HasUnseenGameChanges when a game enters or
        // leaves the set (order relative to P7-03's notification handlers does not matter).
        services.AddDomainEventHandler<GameAddedToSet, GameAddedPickHandler>();
        services.AddDomainEventHandler<GameRemovedFromSet, GameRemovedPickHandler>();

        // Reminder jobs and event notifications (P7-03, Feature 11 section 11). The two Friday
        // crons and the per-week Saturday one-shot; GameAddedToSet/GameRemovedFromSet handlers
        // send immediately.
        services.AddScheduledJob<FridayMemberReminderJob>();
        services.AddScheduledJob<FridayCommissionerSummaryJob>();
        services.AddOneShotJob<SaturdayReminderOneShot>();
        services.AddDomainEventHandler<GameAddedToSet, GamesAddedNotificationHandler>();
        services.AddDomainEventHandler<GameRemovedFromSet, GameRemovedNotificationHandler>();

        // Week scoring (P5-01, Feature 06). The three triggers of 04-Domain-Algorithms.md section
        // 7 - a game going final, a commissioner's correction, and the nightly safety sweep - all
        // funnel into ScoringService's full recompute. IStandingsSnapshotWriter is the seam P5-03
        // fills: its StandingsCalculator-backed writer registers on an earlier line and this
        // TryAdd then no-ops, leaving the logging stand-in behind only while P5-03 is unmerged.
        services.AddScoped<ScoringService>();
        services.TryAddScoped<IStandingsSnapshotWriter, NoOpStandingsSnapshotWriter>();
        services.AddDomainEventHandler<GameWentFinal, GameWentFinalScoringHandler>();
        services.AddDomainEventHandler<ResultOverridden, ResultOverriddenScoringHandler>();
        services.AddDomainEventHandler<GameVoided, GameVoidedScoringHandler>();
        services.AddScheduledJob<NightlyRescoreJob>();
        // Commissioner corrections (P5-02, Feature 06): override-result/void write the row and
        // AuditLog and raise the two events registered just above; this handler only quiets the
        // dispatcher's "no handler" log for GameNeedsVoidReview (P3-04's event).
        services.AddScoped<CorrectionService>();
        services.AddDomainEventHandler<GameNeedsVoidReview, GameNeedsVoidReviewHandler>();

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

        // Provider data refresh jobs (P2-04). Cron times are Eastern, per AGENT-NOTES "Jobs".
        services.AddScoped<ScheduleRefreshRunner>();
        services.AddScoped<RankingsRefreshRunner>();
        services.AddScheduledJob<TeamsRefreshJob>();
        services.AddScheduledJob<ScheduleRefreshJob>();
        services.AddScheduledJob<ScheduleRefreshDailyJob>();
        services.AddScheduledJob<RankingsRefreshEveningJob>();
        services.AddScheduledJob<RankingsRefreshTuesdayJob>();
        services.AddScheduledJob<LinesRefreshJob>();

        // The Saturday live-score poller (P2-04): its own BackgroundService, gated on
        // Jobs:Enabled like the cron scheduler, since it is not cron-driven itself.
        services.AddHostedService<SaturdayPoller>();

        // P8-06: on a fresh database the earliest of those crons is next Tuesday, so the first
        // start fetches the calendar and the current week itself (D-164). No-op unless
        // Providers:ReferenceData is Cfbd with jobs on, or Providers:BootstrapOnStartup says so.
        services.TryAddScoped<ReferenceDataBootstrap>();
        services.AddHostedService<ReferenceDataBootstrapHostedService>();

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
            client.DefaultRequestHeaders.UserAgent.ParseAdd(EspnLiveScoreProvider.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
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

    private static bool IsDevelopmentOrTesting(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    /// <summary>
    /// The dev clock, pre-shifted from configuration: <c>Clock:NowUtc</c> (any ISO 8601 instant)
    /// makes the app boot believing it is that moment, and <c>Clock:Frozen=true</c> stops it
    /// there. Both are optional; with neither the clock reads real time until
    /// <c>PUT /api/admin/fixture/clock</c> moves it.
    /// </summary>
    private static DevTimeProvider CreateDevClock(IServiceProvider services, IConfiguration configuration)
    {
        var clock = new DevTimeProvider();
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<DevTimeProvider>();

        string? configuredNow = configuration["Clock:NowUtc"];
        if (!string.IsNullOrWhiteSpace(configuredNow))
        {
            if (DateTimeOffset.TryParse(
                    configuredNow,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset nowUtc))
            {
                clock.SetNow(nowUtc);
            }
            else
            {
                logger.LogWarning(
                    "Clock:NowUtc '{ConfiguredNow}' is not an ISO 8601 instant; the dev clock starts at real time",
                    configuredNow);
            }
        }

        if (configuration.GetValue<bool>("Clock:Frozen"))
        {
            clock.Freeze();
        }

        if (clock.IsShifted)
        {
            logger.LogWarning(
                "Dev clock is shifted: the app believes it is {AppNowUtc:o} (real {RealNowUtc:o}, frozen={Frozen})",
                clock.GetUtcNow(),
                clock.RealUtcNow,
                clock.IsFrozen);
        }

        return clock;
    }
}
