namespace NcaafPickEm.Infrastructure.Data;

/// <summary>
/// Defaults shared by the design-time factory, the runtime registration, and the test harness.
/// </summary>
public static class DatabaseDefaults
{
    /// <summary>Configuration key holding the application connection string.</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>Configuration key that turns startup migration on or off (D-015).</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>
    /// Configuration key bounding how long startup migration waits for SQL Server to accept
    /// connections before giving up (P8-05). An mssql container takes 20-60 s to come up.
    /// </summary>
    public const string StartupTimeoutSecondsKey = "Database:StartupTimeoutSeconds";

    /// <summary>Default for <see cref="StartupTimeoutSecondsKey"/>.</summary>
    public const int DefaultStartupTimeoutSeconds = 120;

    /// <summary>
    /// The only SQL Server on a developer machine per <c>AGENT-NOTES.md</c>. Used when
    /// <c>ConnectionStrings__Default</c> and <c>TEST_SQL_CONNECTION</c> are both unset.
    /// </summary>
    public const string LocalDbConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=NcaafPickEm;Trusted_Connection=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";

    /// <summary>
    /// The same LocalDB server with no database chosen. Test harnesses append their own
    /// <c>Initial Catalog</c>.
    /// </summary>
    public const string LocalDbServerConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
}
