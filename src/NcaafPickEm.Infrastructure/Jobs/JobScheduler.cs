using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// The one background service that runs every scheduled job in the app (D-005): no Hangfire, no
/// Quartz, no external scheduler.
/// </summary>
/// <remarks>
/// All it does is call <see cref="SchedulerTick.TickAsync"/> once a minute; the deciding, the
/// claiming and the running live there, where a test can drive them at an arbitrary instant. The
/// timer comes from the injected <see cref="TimeProvider"/>, so a test can advance it instead of
/// waiting.
/// </remarks>
public sealed class JobScheduler : BackgroundService
{
    /// <summary>
    /// How often a tick happens. Cron occurrences are minute-granular, and the tick window is
    /// "everything since the last recorded occurrence", so drift never loses one.
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly SchedulerTick _tick;
    private readonly TimeProvider _timeProvider;
    private readonly JobsOptions _options;
    private readonly ILogger<JobScheduler> _logger;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="tick">The scheduling logic.</param>
    /// <param name="timeProvider">Clock and timer source.</param>
    /// <param name="options">The bound <c>Jobs</c> section.</param>
    /// <param name="logger">Structured log sink.</param>
    public JobScheduler(
        SchedulerTick tick,
        TimeProvider timeProvider,
        IOptions<JobsOptions> options,
        ILogger<JobScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _tick = tick;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Job scheduler is off (Jobs:Enabled = false); no scheduled job will run");
            return;
        }

        // Do not hold up host startup with the first tick's database round trip.
        await Task.Yield();

        _logger.LogInformation(
            "Job scheduler started; ticking every {TickSeconds}s with a {CatchUpMinutes}-minute catch-up window",
            TickInterval.TotalSeconds,
            _options.CatchUpMinutes);

        using var timer = new PeriodicTimer(TickInterval, _timeProvider);

        try
        {
            // The first tick happens immediately, so a restart picks up whatever the outage missed
            // without waiting out a whole interval first.
            do
            {
                await TickOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("Job scheduler stopped");
    }

    private async Task TickOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _tick.TickAsync(_timeProvider.GetUtcNow(), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One bad tick (a database outage, say) must not kill the scheduler for the lifetime
            // of the process; the next minute tries again.
            _logger.LogError(exception, "Job scheduler tick failed");
        }
    }
}
