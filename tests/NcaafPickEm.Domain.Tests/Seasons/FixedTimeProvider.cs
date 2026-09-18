namespace NcaafPickEm.Domain.Tests.Seasons;

/// <summary>
/// A clock stuck at one instant. Hand-rolled rather than taking a dependency on
/// Microsoft.Extensions.TimeProvider.Testing: the domain tests only ever need "now is X".
/// </summary>
/// <param name="utcNow">The instant every call returns.</param>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Moves the clock to a new instant.</summary>
    /// <param name="utcNow">The new instant.</param>
    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
}
