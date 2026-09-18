using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A throwaway SQL Server database for one test run: created, migrated, and dropped.
/// </summary>
/// <remarks>
/// The server comes from the <c>TEST_SQL_CONNECTION</c> environment variable and falls back to
/// LocalDB, the only SQL Server on a developer machine (<c>AGENT-NOTES.md</c>). Creating one
/// database per test run rather than per test keeps the whole suite to a single migrate.
/// </remarks>
public sealed class SqlTestDatabase : IAsyncDisposable
{
    /// <summary>Environment variable holding a server-level connection string for tests.</summary>
    public const string ConnectionEnvironmentVariable = "TEST_SQL_CONNECTION";

    private readonly string _serverConnectionString;
    private bool _dropped;

    private SqlTestDatabase(string serverConnectionString, string databaseName, string connectionString)
    {
        _serverConnectionString = serverConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    /// <summary>Name of the database this instance owns, <c>NcaafPickEm_Test_&lt;guid&gt;</c>.</summary>
    public string DatabaseName { get; }

    /// <summary>Connection string pointing at <see cref="DatabaseName"/>.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates the database and applies every migration to it.</summary>
    public static async Task<SqlTestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        string serverConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : DatabaseDefaults.LocalDbServerConnectionString;

        string databaseName = $"NcaafPickEm_Test_{Guid.CreateVersion7():N}";

        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = databaseName,
        };

        var database = new SqlTestDatabase(serverConnectionString, databaseName, builder.ConnectionString);

        await database.ExecuteOnServerAsync($"CREATE DATABASE [{databaseName}];", cancellationToken);

        try
        {
            await using AppDbContext context = database.CreateContext();
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }

        return database;
    }

    /// <summary>Opens a context against this database. The caller disposes it.</summary>
    public AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>Drops the database. Safe to call twice.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_dropped)
        {
            return;
        }

        _dropped = true;

        // Pooled connections would otherwise hold the database open and make the drop fail.
        SqlConnection.ClearAllPools();

        await ExecuteOnServerAsync(
            $"""
             IF DB_ID('{DatabaseName}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{DatabaseName}];
             END
             """,
            CancellationToken.None);
    }

    private async Task ExecuteOnServerAsync(string sql, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = "master",
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
