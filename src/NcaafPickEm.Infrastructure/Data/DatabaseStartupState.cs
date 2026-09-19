namespace NcaafPickEm.Infrastructure.Data;

/// <summary>
/// Whether <see cref="DatabaseMigratorHostedService"/> is still working (P8-05).
/// </summary>
/// <remarks>
/// Kestrel is started by <c>GenericWebHostService</c>, which the web host registers before
/// <c>AddInfrastructure</c> registers the migrator, so the app is already answering HTTP while
/// migrations apply. <c>/health/ready</c> reads this so a container that is mid-migration reports
/// 503 rather than "ready" - Docker's <c>HEALTHCHECK</c> and <c>depends_on: service_healthy</c>
/// both key off that answer.
/// </remarks>
public sealed class DatabaseStartupState
{
    private int _migrating;

    /// <summary>True from the moment startup migration begins until it has applied or failed.</summary>
    public bool IsMigrating => Volatile.Read(ref _migrating) != 0;

    /// <summary>
    /// Called by <see cref="DatabaseMigratorHostedService"/> only. Public rather than internal
    /// so <c>HealthEndpointTests</c> can drive the readiness gate without booting a second host
    /// against a deliberately slow database.
    /// </summary>
    public void BeginMigrating() => Volatile.Write(ref _migrating, 1);

    /// <summary>
    /// Called by <see cref="DatabaseMigratorHostedService"/> in a <c>finally</c>, so a failed
    /// migration does not latch <c>/health/ready</c> at 503 forever.
    /// </summary>
    public void EndMigrating() => Volatile.Write(ref _migrating, 0);
}
