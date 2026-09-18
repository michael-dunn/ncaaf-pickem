using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NcaafPickEm.Infrastructure.Data;

/// <summary>
/// Applies pending migrations before the app starts serving, when configuration allows it (D-013).
/// </summary>
/// <remarks>
/// Default is on in Development and off everywhere else, so a developer never has to remember
/// <c>dotnet ef database update</c> and a production deploy never migrates itself by surprise.
/// Set <c>Database__MigrateOnStartup</c> to override either way. Tests set it to false and migrate
/// through their own throwaway database instead.
/// </remarks>
public sealed class DatabaseMigratorHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DatabaseMigratorHostedService> _logger;

    /// <summary>Creates the migrator.</summary>
    public DatabaseMigratorHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<DatabaseMigratorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
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

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
