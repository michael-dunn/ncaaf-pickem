using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A scheduler wired to the test database with whatever jobs a test wants, without booting the
/// whole app.
/// </summary>
/// <remarks>
/// The shared <see cref="ApiFactory"/> runs with <c>Jobs:Enabled=false</c> and only registers
/// <c>HeartbeatJob</c>, so scheduler tests build their own container over the same database. Every
/// job a test registers must use a unique name (<see cref="UniqueJobName"/>): the database is
/// shared by the whole run and <c>JobRuns</c> is keyed by name.
/// </remarks>
public sealed class SchedulerHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private SchedulerHarness(ServiceProvider provider)
    {
        _provider = provider;
        Tick = provider.GetRequiredService<SchedulerTick>();
    }

    /// <summary>The scheduling logic, to be driven at an instant of the test's choosing.</summary>
    public SchedulerTick Tick { get; }

    /// <summary>Builds a harness over the fixture's database.</summary>
    /// <param name="fixture">The shared fixture, for its connection string.</param>
    /// <param name="configure">Registers the jobs under test.</param>
    /// <param name="enabled">Value of <c>Jobs:Enabled</c>.</param>
    /// <param name="catchUpMinutes">Value of <c>Jobs:CatchUpMinutes</c>.</param>
    public static SchedulerHarness Create(
        ApiTestFixture fixture,
        Action<IServiceCollection> configure,
        bool enabled = true,
        int catchUpMinutes = 60)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(configure);

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(fixture.Database.ConnectionString));
        services.Configure<JobsOptions>(options =>
        {
            options.Enabled = enabled;
            options.CatchUpMinutes = catchUpMinutes;
        });
        services.AddSingleton<SchedulerTick>();

        configure(services);

        return new SchedulerHarness(services.BuildServiceProvider());
    }

    /// <summary>A name no other test in the run can collide with.</summary>
    /// <param name="prefix">Something readable in a failing assertion.</param>
    public static string UniqueJobName(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}"[..24];

    /// <summary>
    /// A five-field cron expression that fires exactly once a year, at the Eastern wall-clock
    /// minute <paramref name="instant"/> falls in.
    /// </summary>
    /// <param name="instant">The instant the job should be scheduled for.</param>
    public static string CronForMinuteOf(DateTimeOffset instant)
    {
        DateTimeOffset eastern = SeasonCalendar.ToEastern(instant);
        return $"{eastern.Minute} {eastern.Hour} {eastern.Day} {eastern.Month} *";
    }

    /// <summary>The real <see cref="JobScheduler"/> hosted service over this harness's container.</summary>
    public JobScheduler CreateHostedScheduler() => ActivatorUtilities.CreateInstance<JobScheduler>(_provider);

    /// <summary>Every <c>JobRuns</c> row whose name starts with <paramref name="jobName"/>.</summary>
    /// <param name="jobName">A cron job's name, or a one-shot source's name.</param>
    public async Task<List<JobRun>> ReadRunsAsync(string jobName)
    {
        await using AsyncServiceScope scope = _provider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.JobRuns
            .AsNoTracking()
            .Where(run => run.JobName == jobName || run.JobName.StartsWith(jobName + ":"))
            .OrderBy(run => run.ScheduledForUtc)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}
