using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Simulator.Engine;
using NcaafPickEm.Simulator.Engine.Views;

namespace NcaafPickEm.Simulator.Tests.Infrastructure;

/// <summary>
/// The simulator's engine, hosted the way <c>Program.cs</c> hosts it - <c>AddSimulator</c> with the
/// forced settings, the migrator and the database guard started - but without the web UI and
/// without the autoplay loop, which the tests tick by hand against a <see cref="MovableClock"/>.
/// </summary>
internal sealed class SimHarness : IAsyncDisposable
{
    /// <summary>A Tuesday afternoon in October: nowhere near a Saturday, which is the point.</summary>
    public static readonly DateTimeOffset TuesdayAfternoonUtc = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    public const int SeasonYear = 2026;

    private readonly IHost _host;

    private SimHarness(IHost host, SimTestDatabase database, MovableClock clock)
    {
        _host = host;
        Database = database;
        Clock = clock;
    }

    public SimTestDatabase Database { get; }

    public MovableClock Clock { get; }

    public SimWorld World => _host.Services.GetRequiredService<SimWorld>();

    public SimDatabaseStatus Status => _host.Services.GetRequiredService<SimDatabaseStatus>();

    public static async Task<SimHarness> StartAsync(DateTimeOffset? startUtc = null, SimTestDatabase? database = null)
    {
        database ??= await SimTestDatabase.CreateAsync();
        var clock = new MovableClock(startUtc ?? TuesdayAfternoonUtc);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = false,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = database.ConnectionString,
            ["Simulator:SeasonYear"] = SeasonYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Configuration.AddInMemoryCollection(SimulatorServiceCollectionExtensions.ForcedSettings);

        // Before AddSimulator: AddInfrastructure only TryAdds the system clock.
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSimulator(builder.Configuration, builder.Environment, runAutoplay: false);

        IHost host = builder.Build();
        await host.StartAsync();

        return new SimHarness(host, database, clock);
    }

    public Task<SimSnapshot> SnapshotAsync() => World.ReadAsync(CancellationToken.None);

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using AppDbContext context = Database.CreateContext();
        return await query(context);
    }

    /// <summary>Creates the default sandbox and fails the test if the log says anything failed.</summary>
    public async Task<SimSnapshot> CreateSandboxAsync(SandboxRequest? request = null)
    {
        SimLog log = await World.CreateSandboxAsync(request ?? new SandboxRequest(), CancellationToken.None);
        log.Failed.Should().BeFalse(string.Join(Environment.NewLine, log.Lines));
        return await SnapshotAsync();
    }

    /// <summary>Asserts the action did not fail and returns its log.</summary>
    public static SimLog Ok(SimLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        log.Failed.Should().BeFalse(string.Join(Environment.NewLine, log.Lines));
        return log;
    }

    /// <summary>Moves time forward and lets autoplay catch up, as the loop would.</summary>
    public async Task PassAsync(TimeSpan time)
    {
        Clock.Advance(time);
        await World.AutoplayTickAsync(CancellationToken.None);
    }

    public async Task<int> CountAsync<T>(Func<AppDbContext, IQueryable<T>> query) =>
        await QueryAsync(context => query(context).CountAsync());

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await Database.DisposeAsync();
    }
}
