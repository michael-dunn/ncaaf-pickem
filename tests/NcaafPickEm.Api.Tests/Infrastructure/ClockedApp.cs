namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A private <see cref="ApiFactory"/> over the run's shared database whose clock the test can
/// move. <see cref="ApiTestFixture.PinnedFactory"/> is fixed at one instant and shared by every
/// class, so anything that needs time to pass — a game added after a member submitted, a week
/// reaching its lock instant — boots its own app with this instead.
/// </summary>
/// <remarks>
/// Create one per test class and dispose it in <c>IAsyncLifetime.DisposeAsync</c>. Booting a host
/// is the expensive part, so do not create one per test.
/// </remarks>
public sealed class ClockedApp : IAsyncDisposable
{
    private readonly FixedTimeProvider _clock;
    private readonly ApiFactory _factory;

    /// <summary>Boots the app with its clock at <paramref name="startUtc"/>.</summary>
    /// <param name="fixture">The shared fixture, for its database.</param>
    /// <param name="startUtc">
    /// The starting instant; defaults to <see cref="ApiTestFixture.PinnedNowUtc"/>, which is
    /// inside the fixture calendar's week <see cref="ApiTestFixture.PinnedCurrentWeek"/>.
    /// </param>
    public ClockedApp(ApiTestFixture fixture, DateTimeOffset? startUtc = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _clock = new FixedTimeProvider(startUtc ?? ApiTestFixture.PinnedNowUtc);
        _factory = new ApiFactory(fixture.Database.ConnectionString, timeProvider: _clock);
    }

    /// <summary>The booted app.</summary>
    public ApiFactory Factory => _factory;

    /// <summary>What the app currently believes "now" is.</summary>
    public DateTimeOffset NowUtc => _clock.GetUtcNow();

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far forward.</param>
    public void Advance(TimeSpan by) => _clock.Set(_clock.GetUtcNow() + by);

    /// <summary>Moves the clock to a specific instant.</summary>
    /// <param name="utcNow">The new "now".</param>
    public void Set(DateTimeOffset utcNow) => _clock.Set(utcNow);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
