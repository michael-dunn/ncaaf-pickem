using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Simulator.Tests.Infrastructure;

/// <summary>
/// A throwaway, migrated SQL Server database per test - the same approach as the API tests'
/// <c>SqlTestDatabase</c>, and the same <c>TEST_SQL_CONNECTION</c> seam (a server-level string;
/// LocalDB by default). Every simulator test gets its own, because the simulator's state is the
/// whole database.
/// </summary>
internal sealed class SimTestDatabase : IAsyncDisposable
{
    private const string ConnectionEnvironmentVariable = "TEST_SQL_CONNECTION";

    private readonly string _serverConnectionString;
    private bool _dropped;

    private SimTestDatabase(string serverConnectionString, string databaseName, string connectionString)
    {
        _serverConnectionString = serverConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public static async Task<SimTestDatabase> CreateAsync()
    {
        string server = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : DatabaseDefaults.LocalDbServerConnectionString;

        string name = $"NcaafPickEm_SimTest_{Guid.CreateVersion7():N}";
        var builder = new SqlConnectionStringBuilder(server) { InitialCatalog = name };
        var database = new SimTestDatabase(server, name, builder.ConnectionString);

        await database.ExecuteOnServerAsync($"CREATE DATABASE [{name}];");

        try
        {
            await using AppDbContext context = database.CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }

        return database;
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        if (_dropped)
        {
            return;
        }

        _dropped = true;
        SqlConnection.ClearAllPools();

        await ExecuteOnServerAsync(
            $"""
             IF DB_ID('{DatabaseName}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{DatabaseName}];
             END
             """);
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        var builder = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
