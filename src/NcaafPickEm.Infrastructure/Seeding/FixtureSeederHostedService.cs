using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NcaafPickEm.Infrastructure.Seeding;

/// <summary>
/// Runs <see cref="FixtureSeeder"/> at startup, after migrations have applied (D-015; registered
/// after <c>DatabaseMigratorHostedService</c> so <c>IHost</c> starts them in order).
/// </summary>
/// <remarks>
/// Only runs when <c>Providers:ReferenceData</c> is <c>Fixture</c> — a real provider owns the data
/// otherwise and this must never race its ingest. <c>Seed:DemoLeague</c> separately controls the
/// demo league (P2-05).
/// </remarks>
public sealed class FixtureSeederHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FixtureSeederHostedService> _logger;

    /// <summary>Creates the hosted service.</summary>
    public FixtureSeederHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<FixtureSeederHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string referenceDataProvider = _configuration["Providers:ReferenceData"] ?? string.Empty;
        if (!string.Equals(referenceDataProvider, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool seedDemoLeague = _configuration.GetValue<bool>("Seed:DemoLeague");

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        FixtureSeeder seeder = scope.ServiceProvider.GetRequiredService<FixtureSeeder>();

        _logger.LogInformation(
            "Running fixture seeder (Seed:DemoLeague={SeedDemoLeague})",
            seedDemoLeague);

        await seeder.SeedAsync(seedDemoLeague, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
