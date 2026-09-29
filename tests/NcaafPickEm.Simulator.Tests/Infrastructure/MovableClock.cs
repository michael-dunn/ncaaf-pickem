namespace NcaafPickEm.Simulator.Tests.Infrastructure;

/// <summary>
/// A clock the test moves by hand - standing in for the passing of real time, which is what the
/// simulator itself never touches. Shared by the simulator host and the app in the same test so
/// both agree on "now".
/// </summary>
internal sealed class MovableClock : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _nowUtc;

    public MovableClock(DateTimeOffset startUtc)
    {
        _nowUtc = startUtc;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _nowUtc;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _nowUtc += by;
        }
    }
}
