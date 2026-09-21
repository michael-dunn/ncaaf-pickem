using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Auth;
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
    private readonly bool _useTestAuth;
    private readonly TimeProvider? _timeProvider;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string>? _settings;

    /// <summary>Creates a factory bound to a database created by <see cref="SqlTestDatabase"/>.</summary>
    /// <param name="connectionString">The test database.</param>
    /// <param name="useTestAuth">
    /// When true (the default) <see cref="TestAuthHandler"/> becomes the default scheme, so tests
    /// sign in with <c>X-Test-User</c>. Pass false to leave the app's own <c>AppAuth</c> policy
    /// scheme in charge — which forwards to the Tailscale identity handler or the cookie
    /// depending on the request's headers — as the Google sign-in tests and
    /// <c>TailscaleAuthTests</c> (P9-02) need. Only the default scheme is replaced either way:
    /// the cookie, Google and Tailscale schemes all stay registered.
    /// </param>
    /// <param name="timeProvider">
    /// When given, replaces <c>TimeProvider.System</c> for the whole app (P1-01 hook: a test that
    /// needs a specific "now" — e.g. a fixed instant inside the 2026 fixture season — passes a
    /// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c> or any other <see cref="TimeProvider"/>
    /// here). <c>AddInfrastructure</c> registers the real clock with <c>TryAddSingleton</c>; this
    /// registers the fake one afterwards in <c>ConfigureTestServices</c>, which runs after the
    /// app's own <c>ConfigureServices</c> and therefore wins.
    /// </param>
    /// <param name="configureServices">
    /// Last-word service overrides, run inside <c>ConfigureTestServices</c> (P7-01 hook: swapping
    /// <c>IPushSender</c> for a scriptable fake). The app has already registered everything by
    /// then, so replacing a service means <c>RemoveAll&lt;T&gt;()</c> first — a <c>TryAdd</c>
    /// here would lose to the real registration.
    /// </param>
    /// <param name="settings">
    /// Configuration overrides applied after this class's own defaults, so a test can boot the
    /// app with e.g. <c>Providers:ReferenceData=Cfbd</c> (P8-06's bootstrap tests, which then
    /// replace the CFBD provider itself through <paramref name="configureServices"/>).
    /// </param>
    public ApiFactory(
        string connectionString,
        bool useTestAuth = true,
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        _connectionString = connectionString;
        _useTestAuth = useTestAuth;
        _timeProvider = timeProvider;
        _configureServices = configureServices;
        _settings = settings;
    }

    /// <summary>A client that is signed in as <paramref name="userId"/> on every request.</summary>
    public HttpClient CreateClientAs(Guid userId, string? displayName = null)
    {
        if (!_useTestAuth)
        {
            throw new InvalidOperationException(
                "This factory runs the real cookie scheme; sign in through /auth instead.");
        }

        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());

        if (displayName is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, displayName);
        }

        return client;
    }

    /// <summary>A client that always sends the CSRF header, for tests about something else.</summary>
    public HttpClient CreateMutatingClientAs(Guid userId)
    {
        HttpClient client = CreateClientAs(userId);
        client.DefaultRequestHeaders.Add(AuthDefaults.CsrfHeaderName, AuthDefaults.CsrfHeaderValue);
        return client;
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

        // SqlTestDatabase already migrated; the app must not race it at startup (D-015).
        builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");

        // Every test request arrives with no remote IP, so they would all share one rate-limit
        // partition and a long suite would trip it (P8-01, D-153). RateLimitTests boots its own
        // host with the limiter on and a tiny window.
        builder.UseSetting(RateLimitingSetup.EnabledKey, "false");

        foreach ((string key, string value) in _settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        if (_useTestAuth)
        {
            // Runs after the app's own registration, so this Configure wins and TestAuth becomes
            // the default authenticate/challenge scheme. The cookie scheme stays registered.
            builder.ConfigureTestServices(services =>
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { }));
        }

        if (_timeProvider is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(_timeProvider));
        }

        if (_configureServices is not null)
        {
            builder.ConfigureTestServices(_configureServices);
        }
    }
}
