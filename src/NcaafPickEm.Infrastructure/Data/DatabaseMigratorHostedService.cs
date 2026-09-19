using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NcaafPickEm.Infrastructure.Data;

/// <summary>
/// Applies pending migrations before the app starts serving, when configuration allows it (D-015).
/// </summary>
/// <remarks>
/// Default is on in Development and off everywhere else, so a developer never has to remember
/// <c>dotnet ef database update</c> and a production deploy never migrates itself by surprise.
/// Set <c>Database__MigrateOnStartup</c> to override either way. Tests set it to false and migrate
/// through their own throwaway database instead. The Docker deployment sets it to true (D-159):
/// there is no separate deploy step there, only <c>docker compose up -d</c>.
/// <para>
/// When it is on, the first thing it does is wait for SQL Server to accept connections, retrying
/// with backoff for up to <c>Database__StartupTimeoutSeconds</c> (default 120). An mssql container
/// started alongside this one takes 20-60 s to come up, and a compose <c>depends_on</c> healthcheck
/// only covers the first start - a host reboot brings both back at once.
/// </para>
/// </remarks>
public sealed class DatabaseMigratorHostedService : IHostedService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly DatabaseStartupState _startupState;
    private readonly ILogger<DatabaseMigratorHostedService> _logger;

    /// <summary>Creates the migrator.</summary>
    public DatabaseMigratorHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        DatabaseStartupState startupState,
        ILogger<DatabaseMigratorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _startupState = startupState;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        bool enabled = _configuration.GetValue<bool?>(DatabaseDefaults.MigrateOnStartupKey)
            ?? _environment.IsDevelopment();

        if (!enabled)
        {
            _logger.LogInformation(
                "Startup migration is off for environment {Environment}; run dotnet ef database update by hand",
                _environment.EnvironmentName);
            return;
        }

        int timeoutSeconds = _configuration.GetValue<int?>(DatabaseDefaults.StartupTimeoutSecondsKey)
            ?? DatabaseDefaults.DefaultStartupTimeoutSeconds;

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _startupState.BeginMigrating();
        try
        {
            await WaitForDatabaseAsync(database, timeoutSeconds, cancellationToken);
            await ApplyMigrationsAsync(database, cancellationToken);
        }
        finally
        {
            _startupState.EndMigrating();
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Blocks until SQL Server answers, or until <paramref name="timeoutSeconds"/> elapses, in
    /// which case it throws rather than letting the app serve against a database it cannot reach.
    /// </summary>
    /// <remarks>
    /// No clock is read here (05-Conventions.md forbids <c>DateTime.UtcNow</c>): the deadline is a
    /// linked <see cref="CancellationTokenSource"/> with <c>CancelAfter</c>, which also aborts the
    /// in-flight connection attempt when it expires.
    /// </remarks>
    private async Task WaitForDatabaseAsync(
        AppDbContext database,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        string connectionString = database.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "ConnectionStrings__Default is not set, so startup migration has nothing to " +
                "migrate. Set it, or turn Database__MigrateOnStartup off.");

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        TimeSpan delay = InitialRetryDelay;
        Exception? lastFailure = null;
        int attempt = 0;

        while (!deadline.IsCancellationRequested)
        {
            attempt++;

            try
            {
                await ProbeServerAsync(connectionString, deadline.Token);

                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "SQL Server accepted a connection on attempt {Attempt}", attempt);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                // Either the host is shutting down or the deadline expired; both are handled below.
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                // A container that has not finished starting refuses the socket outright; a bad
                // connection string throws InvalidOperationException instead, and waiting the
                // full timeout before saying so is the clearest failure available.
                lastFailure = ex;
            }

            if (deadline.IsCancellationRequested)
            {
                break;
            }

            _logger.LogWarning(
                lastFailure,
                "SQL Server is not reachable yet (attempt {Attempt}); retrying in {Delay}",
                attempt,
                delay);

            try
            {
                await Task.Delay(delay, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                break;
            }

            delay = delay < MaximumRetryDelay ? delay * 2 : MaximumRetryDelay;
        }

        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(
            $"SQL Server was not reachable within {timeoutSeconds}s ({attempt} attempt(s)). " +
            "Check ConnectionStrings__Default and the database container, or raise " +
            "Database__StartupTimeoutSeconds.",
            lastFailure);
    }

    /// <summary>
    /// Opens a connection to <c>master</c> on the configured server.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Database.CanConnectAsync()</c>: that one opens the *application*
    /// database and answers false when it does not exist yet, which is precisely the state of a
    /// brand-new container the first time it comes up — so the wait would spin out its whole
    /// timeout and fail on exactly the deploy it exists to support (found while validating the
    /// P8-05 compose stack). It also swallows every exception into a bare <c>false</c>, leaving
    /// nothing to log. <c>master</c> is readable by any login that can sign in at all, so this
    /// asks only the question being waited on: is SQL Server accepting connections?
    /// </remarks>
    private static async Task ProbeServerAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
    }

    private async Task ApplyMigrationsAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        IEnumerable<string> pending = await database.Database.GetPendingMigrationsAsync(cancellationToken);
        string[] names = [.. pending];
        if (names.Length == 0)
        {
            _logger.LogInformation("Database schema is current; no migrations to apply");
            return;
        }

        _logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", names.Length, names);
        await database.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Database schema is now current");
    }
}
