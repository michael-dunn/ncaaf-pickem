using Cronos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Infrastructure.Jobs;

/// <summary>
/// One pass of the scheduler: work out what is due, claim it in <c>JobRuns</c>, run it, record
/// the outcome. This is the whole of the scheduling logic, separated from
/// <see cref="JobScheduler"/> so tests can drive it at an arbitrary instant instead of waiting on
/// the wall clock.
/// </summary>
/// <remarks>
/// Idempotency (D-005) rests on the unique index <c>JobRuns(JobName, ScheduledForUtc)</c>. A run
/// is claimed by inserting its row <em>before</em> the job is invoked; a unique-key violation
/// means some other tick — or this process before it restarted — already owns that occurrence, so
/// the job is skipped rather than run twice.
/// </remarks>
public sealed class SchedulerTick
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly JobsOptions _options;
    private readonly ILogger<SchedulerTick> _logger;

    /// <summary>Creates the tick.</summary>
    /// <param name="scopeFactory">Every job run gets its own DI scope from here.</param>
    /// <param name="timeProvider">Clock for the <c>StartedUtc</c> / <c>FinishedUtc</c> stamps.</param>
    /// <param name="options">The bound <c>Jobs</c> section.</param>
    /// <param name="logger">Structured log sink.</param>
    public SchedulerTick(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<JobsOptions> options,
        ILogger<SchedulerTick> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs everything due at <paramref name="nowUtc"/>: cron jobs first, then one-shots.
    /// </summary>
    /// <param name="nowUtc">The instant to schedule against. Callers pass the injected clock.</param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    /// <remarks>
    /// Deliberately does <em>not</em> consult <see cref="JobsOptions.Enabled"/>: that flag switches
    /// the hosted service off, and a caller driving a tick by hand has already decided it wants
    /// one. A failing job never stops the pass; its <c>JobRuns</c> row records the error and the
    /// next job runs.
    /// </remarks>
    public async Task TickAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await RunCronJobsAsync(nowUtc, cancellationToken);
        await RunOneShotJobsAsync(nowUtc, cancellationToken);
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    private static string Describe(Exception exception)
    {
        string text = exception.ToString();
        return text.Length <= JobRun.ErrorMaxLength ? text : text[..JobRun.ErrorMaxLength];
    }

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static TJob Resolve<TJob>(IServiceProvider provider, Func<TJob, string> nameOf, string name)
        where TJob : class =>
        provider.GetServices<TJob>().First(job => string.Equals(nameOf(job), name, StringComparison.Ordinal));

    private async Task RunCronJobsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        CronJobDescriptor[] descriptors = await DescribeCronJobsAsync();
        if (descriptors.Length == 0)
        {
            return;
        }

        Dictionary<string, DateTime> lastOccurrences = await LoadLastOccurrencesAsync(
            [.. descriptors.Select(descriptor => descriptor.Name)], cancellationToken);

        DateTimeOffset floor = nowUtc.AddMinutes(-_options.CatchUpMinutes);

        foreach (CronJobDescriptor descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CronExpression cron;
            try
            {
                cron = CronExpression.Parse(descriptor.CronExpression);
            }
            catch (CronFormatException exception)
            {
                _logger.LogError(
                    exception,
                    "Job {JobName} has an invalid cron expression {CronExpression} and will never run",
                    descriptor.Name,
                    descriptor.CronExpression);
                continue;
            }

            // The window is exclusive at the bottom so the newest occurrence already recorded is
            // not offered again, and inclusive at the top so an occurrence landing exactly on this
            // minute runs now rather than a minute late. Its floor is the catch-up bound, so an
            // outage replays at most JobsOptions.CatchUpMinutes of schedule.
            DateTimeOffset from = lastOccurrences.TryGetValue(descriptor.Name, out DateTime last)
                ? Later(new DateTimeOffset(last, TimeSpan.Zero), floor)
                : floor;

            if (from >= nowUtc)
            {
                continue;
            }

            foreach (DateTimeOffset occurrence in cron.GetOccurrences(
                from, nowUtc, SeasonCalendar.Eastern, fromInclusive: false, toInclusive: true))
            {
                await ExecuteAsync(
                    descriptor.Name,
                    occurrence,
                    (provider, token) =>
                        Resolve<IScheduledJob>(provider, job => job.Name, descriptor.Name).RunAsync(occurrence, token),
                    cancellationToken);
            }
        }
    }

    private async Task RunOneShotJobsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        string[] sources = await DescribeOneShotJobsAsync();

        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<OneShotOccurrence> due;
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                due = await Resolve<IOneShotJob>(scope.ServiceProvider, job => job.Name, source)
                    .GetDueAsync(nowUtc, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "One-shot source {JobName} failed to report what is due", source);
                continue;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (OneShotOccurrence occurrence in due)
            {
                // A source is allowed to answer with everything it knows about; the scheduler is
                // the one that decides "due" means "not in the future".
                if (occurrence.DueUtc > nowUtc || string.IsNullOrWhiteSpace(occurrence.Key))
                {
                    continue;
                }

                string runName = $"{source}:{occurrence.Key}";
                if (runName.Length > JobRun.JobNameMaxLength)
                {
                    _logger.LogError(
                        "One-shot {RunName} exceeds the {Limit}-character JobRuns.JobName column and was skipped",
                        runName,
                        JobRun.JobNameMaxLength);
                    continue;
                }

                if (!seen.Add(runName))
                {
                    continue;
                }

                await ExecuteAsync(
                    runName,
                    occurrence.DueUtc,
                    (provider, token) =>
                        Resolve<IOneShotJob>(provider, job => job.Name, source).RunAsync(occurrence, token),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Claims <paramref name="occurrence"/> for <paramref name="jobName"/>, runs the work in its
    /// own scope, and records the outcome.
    /// </summary>
    private async Task ExecuteAsync(
        string jobName,
        DateTimeOffset occurrence,
        Func<IServiceProvider, CancellationToken, Task> invoke,
        CancellationToken cancellationToken)
    {
        // A dedicated scope for the bookkeeping, held open across the run so the claimed row can
        // be updated afterwards. The job itself gets a separate scope, and therefore its own
        // AppDbContext, so it cannot disturb (or be disturbed by) the JobRuns row.
        await using AsyncServiceScope bookkeeping = _scopeFactory.CreateAsyncScope();
        AppDbContext database = bookkeeping.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = new JobRun
        {
            Id = Guid.CreateVersion7(),
            JobName = jobName,
            ScheduledForUtc = occurrence.UtcDateTime,
            StartedUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Success = false,
        };

        database.JobRuns.Add(run);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // Already claimed: a restart replaying the catch-up window, or a second instance.
            database.Entry(run).State = EntityState.Detached;
            _logger.LogDebug(
                "Job {JobName} occurrence {ScheduledFor:o} was already recorded; skipping",
                jobName,
                occurrence);
            return;
        }

        _logger.LogInformation("Job {JobName} starting for occurrence {ScheduledFor:o}", jobName, occurrence);

        try
        {
            await using AsyncServiceScope work = _scopeFactory.CreateAsyncScope();
            await invoke(work.ServiceProvider, cancellationToken);

            run.Success = true;
            _logger.LogInformation("Job {JobName} finished for occurrence {ScheduledFor:o}", jobName, occurrence);
        }
        catch (Exception exception)
        {
            run.Success = false;
            run.Error = Describe(exception);
            _logger.LogError(exception, "Job {JobName} failed for occurrence {ScheduledFor:o}", jobName, occurrence);
        }

        run.FinishedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // The outcome is recorded even when the host is shutting down; otherwise the row stays
        // claimed with no finish time and reads as a run that never came back.
        await database.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<CronJobDescriptor[]> DescribeCronJobsAsync()
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        CronJobDescriptor[] descriptors =
        [
            .. scope.ServiceProvider.GetServices<IScheduledJob>()
                .Select(job => new CronJobDescriptor(job.Name, job.CronExpression)),
        ];

        WarnOnDuplicates([.. descriptors.Select(descriptor => descriptor.Name)], nameof(IScheduledJob));
        return descriptors;
    }

    private async Task<string[]> DescribeOneShotJobsAsync()
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        string[] names = [.. scope.ServiceProvider.GetServices<IOneShotJob>().Select(job => job.Name)];

        WarnOnDuplicates(names, nameof(IOneShotJob));
        return names;
    }

    private void WarnOnDuplicates(string[] names, string kind)
    {
        foreach (IGrouping<string, string> group in names.GroupBy(name => name, StringComparer.Ordinal))
        {
            int count = group.Count();
            if (count > 1)
            {
                _logger.LogError(
                    "{Count} registered {Kind} implementations share the name {JobName}; their runs "
                    + "deduplicate against each other and all but one will be skipped",
                    count,
                    kind,
                    group.Key);
            }
        }
    }

    private async Task<Dictionary<string, DateTime>> LoadLastOccurrencesAsync(
        string[] jobNames,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.JobRuns
            .AsNoTracking()
            .Where(run => jobNames.Contains(run.JobName))
            .GroupBy(run => run.JobName)
            .Select(group => new { JobName = group.Key, Latest = group.Max(run => run.ScheduledForUtc) })
            .ToDictionaryAsync(row => row.JobName, row => row.Latest, StringComparer.Ordinal, cancellationToken);
    }

    private sealed record CronJobDescriptor(string Name, string CronExpression);
}
