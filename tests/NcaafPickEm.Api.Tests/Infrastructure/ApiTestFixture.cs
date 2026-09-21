namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// One database and two booted apps for the whole API test run.
/// </summary>
/// <remarks>
/// Used through <see cref="ApiTestCollection"/>, so xUnit creates it once, shares it across every
/// test class in the collection, and drops the database when the last one finishes.
/// </remarks>
public sealed class ApiTestFixture : IAsyncLifetime
{
    /// <summary>
    /// 2026-10-14 12:00 UTC (08:00 ET, a Wednesday) falls inside the fixture calendar's Week 7
    /// window (Sun 2026-10-11 through Sat 2026-10-17 ET). Tests that need a known "current week"
    /// use <see cref="PinnedFactory"/> instead of <see cref="Factory"/>.
    /// </summary>
    public static readonly DateTimeOffset PinnedNowUtc = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The week <see cref="PinnedNowUtc"/> falls in, in the 2026 fixture calendar.</summary>
    public const int PinnedCurrentWeek = 7;

    private SqlTestDatabase? _database;
    private ApiFactory? _factory;
    private ApiFactory? _cookieFactory;
    private ApiFactory? _pinnedFactory;

    /// <summary>The throwaway database for this run.</summary>
    public SqlTestDatabase Database => _database
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>The app with <see cref="TestAuthHandler"/> as the default scheme. Use this.</summary>
    public ApiFactory Factory => _factory
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>
    /// The same app with the shipped scheme set in charge — the <c>AppAuth</c> policy scheme
    /// picking between Tailscale identity headers and the cookie — rather than
    /// <see cref="TestAuthHandler"/>. Used by the Google sign-in tests and by
    /// <c>TailscaleAuthTests</c> (P9-02). Shares the database with <see cref="Factory"/>.
    /// </summary>
    public ApiFactory CookieFactory => _cookieFactory
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>
    /// The same app and database as <see cref="Factory"/>, but with the clock pinned at
    /// <see cref="PinnedNowUtc"/> (week <see cref="PinnedCurrentWeek"/>). Use this whenever a test
    /// depends on "now" — invite expiry, join week, current-week status — rather than whatever
    /// week the wall clock happens to be in when the suite runs.
    /// </summary>
    public ApiFactory PinnedFactory => _pinnedFactory
        ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _factory = new ApiFactory(_database.ConnectionString);
        _cookieFactory = new ApiFactory(_database.ConnectionString, useTestAuth: false);
        _pinnedFactory = new ApiFactory(_database.ConnectionString, timeProvider: new FixedTimeProvider(PinnedNowUtc));
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_pinnedFactory is not null)
        {
            await _pinnedFactory.DisposeAsync();
        }

        if (_cookieFactory is not null)
        {
            await _cookieFactory.DisposeAsync();
        }

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
