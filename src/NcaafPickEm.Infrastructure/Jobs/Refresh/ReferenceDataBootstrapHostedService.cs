using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NcaafPickEm.Infrastructure.Jobs.Refresh;

/// <summary>
/// Runs <see cref="ReferenceDataBootstrap"/> once, in the background, just after startup (P8-06,
/// D-164).
/// </summary>
/// <remarks>
/// <para>
/// Registered after <c>DatabaseMigratorHostedService</c>, so the schema is already in place when
/// this starts; it then yields immediately and does its work off the startup path. The
/// <c>/health/ready</c> gate stays migration-only on purpose - the bootstrap is a handful of
/// provider calls and the app is perfectly serviceable while it runs (the create-league page
/// says the calendar is on its way), whereas holding readiness open would make Docker's
/// <c>HEALTHCHECK</c> and compose's <c>service_healthy</c> wait on CFBD.
/// </para>
/// <para>
/// It may overlap the job scheduler's first tick. Both go through
/// <c>ReferenceDataIngestService</c>, which is idempotent, so the worst case is the same rows
/// being upserted twice.
/// </para>
/// </remarks>
public sealed class ReferenceDataBootstrapHostedService : BackgroundService
{
    /// <summary>
    /// Configuration key that forces the bootstrap on or off
    /// (<c>Providers__BootstrapOnStartup</c>). Unset means "on when
    /// <c>Providers:ReferenceData</c> is <c>Cfbd</c> and <c>Jobs:Enabled</c> is true".
    /// </summary>
    public const string EnabledKey = "Providers:BootstrapOnStartup";

    /// <summary>Configuration key naming the reference data provider.</summary>
    private const string ReferenceDataProviderKey = "Providers:ReferenceData";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly JobsOptions _jobs;
    private readonly ILogger<ReferenceDataBootstrapHostedService> _logger;
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the hosted service.</summary>
    public ReferenceDataBootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<JobsOptions> jobs,
        ILogger<ReferenceDataBootstrapHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _jobs = jobs.Value;
        _logger = logger;
    }

    /// <summary>
    /// Completes when the bootstrap has run, been skipped, or failed. Exists so a test can await
    /// a background service instead of polling the database.
    /// </summary>
    public Task Completed => _completed.Task;

    /// <summary>The result of the run, or null when it was disabled or has not finished.</summary>
    public ReferenceDataBootstrapResult? Result { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!IsEnabled())
            {
                return;
            }

            // Everything after this point is off the host's startup path.
            await Task.Yield();

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ReferenceDataBootstrap bootstrap = scope.ServiceProvider.GetRequiredService<ReferenceDataBootstrap>();

            Result = await bootstrap.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down before the bootstrap finished; the refresh jobs own the
            // data from here.
        }
        catch (Exception ex)
        {
            // A provider failure is already handled inside the ingest; this is the database or
            // the container itself, and it must not take the host down with it.
            _logger.LogWarning(
                ex,
                "Reference data bootstrap failed; the scheduled refresh jobs will retry on their schedules");
        }
        finally
        {
            _completed.TrySetResult();
        }
    }

    /// <summary>
    /// On when <c>Providers:BootstrapOnStartup</c> says so; otherwise on only for the real
    /// provider with jobs enabled - which is exactly a deployed instance, and never a test host
    /// or an offline fixture run.
    /// </summary>
    private bool IsEnabled()
    {
        bool isCfbd = string.Equals(
            _configuration[ReferenceDataProviderKey],
            "Cfbd",
            StringComparison.OrdinalIgnoreCase);

        bool enabled = _configuration.GetValue<bool?>(EnabledKey) ?? (isCfbd && _jobs.Enabled);

        if (!enabled)
        {
            _logger.LogInformation(
                "Reference data bootstrap is off (provider {Provider}, Jobs:Enabled {JobsEnabled})",
                _configuration[ReferenceDataProviderKey] ?? "unset",
                _jobs.Enabled);
            return false;
        }

        if (!isCfbd)
        {
            // Explicitly turned on against the fixture provider: harmless (the fixture payloads
            // would just be re-ingested) but never what was meant on a real deployment.
            _logger.LogWarning(
                "Reference data bootstrap is on but the reference data provider is {Provider}",
                _configuration[ReferenceDataProviderKey] ?? "unset");
        }

        return true;
    }
}
