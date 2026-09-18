using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Infrastructure.Data;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real app against a throwaway database, with jobs off and every provider on fixtures.
/// </summary>
/// <remarks>
/// Share one instance per test run through <see cref="ApiTestFixture"/>; building the host is by
/// far the most expensive thing an API test does.
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    /// <summary>Creates a factory bound to a database created by <see cref="SqlTestDatabase"/>.</summary>
    public ApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Runs <paramref name="action"/> against the test database inside its own scope.</summary>
    public async Task ExecuteDbAsync(Func<AppDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using AsyncServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(database);
    }

    /// <summary>Runs <paramref name="query"/> against the test database and returns its result.</summary>
    public async Task<TResult> QueryDbAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using AsyncServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(database);
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);

        // 05-Conventions.md: API tests never run jobs and never touch a live provider.
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Providers:ReferenceData", "Fixture");
        builder.UseSetting("Providers:LiveScores", "Fixture");

        // SqlTestDatabase already migrated; the app must not race it at startup (D-013).
        builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");
    }
}
