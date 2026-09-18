namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A clock stuck at one instant, for tests that need a specific "now" (e.g. pinned inside the
/// 2026 fixture season so <c>SeasonCalendar</c> resolves a known current week). Pass to
/// <see cref="ApiFactory"/>'s <c>timeProvider</c> parameter.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Moves the clock to a new instant.</summary>
    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
}
