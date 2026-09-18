using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NcaafPickEm.Infrastructure.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build an <see cref="AppDbContext"/> without booting the API host.
/// </summary>
/// <remarks>
/// The connection string comes from the <c>ConnectionStrings__Default</c> environment variable and
/// falls back to LocalDB, so <c>dotnet ef migrations add</c> works on a clean clone with no config
/// at all and <c>dotnet ef database update</c> targets whatever that variable points at. See the
/// "EF Core migrations" section of the root README.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : DatabaseDefaults.LocalDbConnectionString;

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
