using System.Diagnostics;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A clock that starts at a chosen instant and then runs at real speed. For tests that need a
/// known week (like <see cref="FixedTimeProvider"/>) <em>and</em> need successive actions to get
/// successive timestamps - submit, then add a game, then add another - which a frozen clock
/// stamps identically.
/// </summary>
/// <param name="startUtc">The instant the clock reads when created.</param>
public sealed class TickingTimeProvider(DateTimeOffset startUtc) : TimeProvider
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => startUtc + _elapsed.Elapsed;
}
