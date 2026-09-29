using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Runs once at startup, after the app's migrator has brought the schema up to date, and decides
/// whether this database is the simulator's to write (D-186): one it created (it has
/// <c>sim.Settings</c>), or an empty one it may claim. A database with leagues or users but no
/// <c>sim.Settings</c> belongs to somebody - a Fixture dev copy, or worse - and is refused.
/// </summary>
/// <remarks>
/// Registered after <c>AddInfrastructure</c>, so <c>IHost</c> starts it after
/// <c>DatabaseMigratorHostedService</c>, whose <c>StartAsync</c> does the migrating.
/// </remarks>
public sealed class SimDatabaseGuard : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SimDatabaseStatus _status;
    private readonly ILogger<SimDatabaseGuard> _logger;

    /// <summary>Creates the guard.</summary>
    public SimDatabaseGuard(IServiceScopeFactory scopeFactory, SimDatabaseStatus status, ILogger<SimDatabaseGuard> logger)
    {
        _scopeFactory = scopeFactory;
        _status = status;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        SimStateStore store = scope.ServiceProvider.GetRequiredService<SimStateStore>();
        string name = database.Database.GetDbConnection().Database;

        try
        {
            if (await store.TableExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                _status.Accept(name);
                _logger.LogInformation("Simulator database {Database} is ours; ready", name);
                return;
            }

            int leagues = await database.Leagues.CountAsync(cancellationToken).ConfigureAwait(false);
            int users = await database.Users.CountAsync(cancellationToken).ConfigureAwait(false);

            if (leagues + users > 0)
            {
                string problem =
                    $"Database '{name}' was not created by the simulator: it already holds {leagues} league(s) and " +
                    $"{users} user(s). The simulator rewrites games, scores and the calendar at will, so it only " +
                    "works on its own database. Point ConnectionStrings:Default at an empty one (the default is " +
                    "NcaafPickEm_Sim) and run the app against the same one with --launch-profile simulator.";
                _status.Refuse(name, problem);
                _logger.LogError("Simulator refused database {Database}: {Problem}", name, problem);
                return;
            }

            await store.CreateTableAsync(cancellationToken).ConfigureAwait(false);
            _status.Accept(name);
            _logger.LogInformation("Simulator claimed the empty database {Database}", name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            string problem = $"Could not check database '{name}': {exception.Message}";
            _status.Refuse(name, problem);
            _logger.LogError(exception, "Simulator could not check database {Database}", name);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
