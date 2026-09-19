using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-05 / D-159: the startup migrator waits for SQL Server instead of dying on the first refusal.
/// </summary>
/// <remarks>
/// In the Docker deployment the API and the mssql container start together and mssql takes
/// 20-60 s to accept connections. Compose's <c>depends_on: service_healthy</c> covers the first
/// <c>up</c>, but a host reboot brings both back at once with no such ordering, so the app has to
/// tolerate it itself.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class DatabaseMigratorStartupTests
{
    // Nothing is listening here, so every attempt is refused immediately rather than hanging.
    private const string UnreachableServer =
        "Server=127.0.0.1,14399;Database=NcaafPickEm;User Id=sa;Password=not-a-real-password;" +
        "Encrypt=False;TrustServerCertificate=True;Connect Timeout=1";

    private readonly ApiTestFixture _fixture;

    public DatabaseMigratorStartupTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnUnreachableServer_WhenStartupMigrationRuns_ThenItRetriesAndThenFailsFast()
    {
        var log = new RecordingLogger();
        DatabaseMigratorHostedService migrator = CreateMigrator(
            UnreachableServer,
            startupTimeoutSeconds: 3,
            log,
            out DatabaseStartupState state);

        Func<Task> start = () => migrator.StartAsync(CancellationToken.None);

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not reachable within 3s*");

        log.Warnings.Should().HaveCountGreaterThanOrEqualTo(
            2,
            "each failed attempt is logged, so an operator watching `docker compose logs api` " +
            "can see it is waiting rather than wedged");

        state.IsMigrating.Should().BeFalse("a failed migration must not latch /health/ready at 503");
    }

    [Fact]
    public async Task GivenAReachableServer_WhenStartupMigrationRuns_ThenItCompletesAndClearsTheGate()
    {
        var log = new RecordingLogger();
        DatabaseMigratorHostedService migrator = CreateMigrator(
            _fixture.Database.ConnectionString,
            startupTimeoutSeconds: 30,
            log,
            out DatabaseStartupState state);

        await migrator.StartAsync(CancellationToken.None);

        // SqlTestDatabase already migrated this one, so the only interesting part is that the
        // connectivity wait let it straight through and the readiness gate reopened.
        log.Warnings.Should().BeEmpty();
        state.IsMigrating.Should().BeFalse();
    }

    [Fact]
    public async Task GivenTheDatabaseDoesNotExistYet_WhenStartupMigrationRuns_ThenItCreatesAndMigratesIt()
    {
        // The regression this whole class exists for. A brand-new container's first `up` points
        // at a server whose NcaafPickEm database has never been created, and
        // Database.CanConnectAsync() answers false in exactly that state - so gating the wait on
        // it spun out the full timeout and failed on the one deploy it was written to support.
        // The wait probes `master` instead, and the migrator then creates the database.
        var builder = new SqlConnectionStringBuilder(_fixture.Database.ConnectionString)
        {
            InitialCatalog = $"NcaafPickEm_Fresh_{Guid.CreateVersion7():N}",
        };

        var log = new RecordingLogger();
        DatabaseMigratorHostedService migrator = CreateMigrator(
            builder.ConnectionString,
            startupTimeoutSeconds: 30,
            log,
            out DatabaseStartupState state);

        try
        {
            await migrator.StartAsync(CancellationToken.None);

            log.Warnings.Should().BeEmpty("the server was up all along; only the database was missing");
            state.IsMigrating.Should().BeFalse();

            await using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(builder.ConnectionString).Options);

            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            (await context.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        }
        finally
        {
            await DropAsync(builder);
        }
    }

    [Fact]
    public async Task GivenMigrationIsOff_WhenStartupRuns_ThenTheUnreachableServerIsNeverTouched()
    {
        var log = new RecordingLogger();
        DatabaseMigratorHostedService migrator = CreateMigrator(
            UnreachableServer,
            startupTimeoutSeconds: 3,
            log,
            out DatabaseStartupState state,
            migrateOnStartup: false);

        // D-015 is unchanged by P8-05: the Windows-service deployment still migrates as its own
        // explicit deploy step, and must boot without ever waiting on the database here.
        await migrator.StartAsync(CancellationToken.None);

        log.Warnings.Should().BeEmpty();
        state.IsMigrating.Should().BeFalse();
    }

    private static async Task DropAsync(SqlConnectionStringBuilder database)
    {
        SqlConnection.ClearAllPools();

        var master = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master" };

        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
             IF DB_ID('{database.InitialCatalog}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{database.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{database.InitialCatalog}];
             END
             """;
        await command.ExecuteNonQueryAsync();
    }

    private static DatabaseMigratorHostedService CreateMigrator(
        string connectionString,
        int startupTimeoutSeconds,
        ILogger<DatabaseMigratorHostedService> logger,
        out DatabaseStartupState state,
        bool migrateOnStartup = true)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseDefaults.MigrateOnStartupKey] = migrateOnStartup ? "true" : "false",
                [DatabaseDefaults.StartupTimeoutSecondsKey] = startupTimeoutSeconds.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        ServiceProvider provider = services.BuildServiceProvider();

        state = new DatabaseStartupState();

        return new DatabaseMigratorHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            new TestHostEnvironment(),
            state,
            logger);
    }

    /// <summary>Counts the "not reachable yet" warnings without needing a logging framework.</summary>
    private sealed class RecordingLogger : ILogger<DatabaseMigratorHostedService>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings => _warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel >= LogLevel.Warning)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }

    /// <summary>Production, so the migrator never falls back to the Development default.</summary>
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "NcaafPickEm.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
