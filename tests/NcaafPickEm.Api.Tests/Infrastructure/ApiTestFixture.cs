namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// One database and one booted app for the whole API test run.
/// </summary>
/// <remarks>
/// Used through <see cref="ApiTestCollection"/>, so xUnit creates it once, shares it across every
/// test class in the collection, and drops the database when the last one finishes.
/// </remarks>
public sealed class ApiTestFixture : IAsyncLifetime
{
    private SqlTestDatabase? _database;
    private ApiFactory? _factory;

    /// <summary>The throwaway database for this run.</summary>
    public SqlTestDatabase Database => _database
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>The booted app.</summary>
    public ApiFactory Factory => _factory
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _factory = new ApiFactory(_database.ConnectionString);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}
