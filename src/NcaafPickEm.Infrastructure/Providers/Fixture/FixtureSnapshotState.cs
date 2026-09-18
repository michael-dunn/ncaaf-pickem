namespace NcaafPickEm.Infrastructure.Providers.Fixture;

/// <summary>
/// The current position (1 through <see cref="MaxSnapshot"/>) in the Week 7, 2026 fixture score
/// timeline. A singleton so every request in the process sees the same snapshot until something
/// advances it: a test hook, or <c>POST /api/admin/fixture/snapshot/{n}</c> in Development.
/// </summary>
public sealed class FixtureSnapshotState
{
    /// <summary>Lowest legal snapshot index. Kickoff, everything Scheduled.</summary>
    public const int MinSnapshot = 1;

    /// <summary>Highest legal snapshot index. Everything Final, including the post-midnight finish.</summary>
    public const int MaxSnapshot = 6;

    private int _current = MinSnapshot;

    /// <summary>The snapshot index every <see cref="FixtureLiveScoreProvider"/> call reads.</summary>
    public int Current => _current;

    /// <summary>Sets the current snapshot. Throws if outside <see cref="MinSnapshot"/>..<see cref="MaxSnapshot"/>.</summary>
    public void Set(int snapshot)
    {
        if (snapshot < MinSnapshot || snapshot > MaxSnapshot)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                snapshot,
                $"Snapshot must be between {MinSnapshot} and {MaxSnapshot}.");
        }

        _current = snapshot;
    }

    /// <summary>Advances to the next snapshot, clamped at <see cref="MaxSnapshot"/>.</summary>
    public int Advance()
    {
        _current = Math.Min(_current + 1, MaxSnapshot);
        return _current;
    }

    /// <summary>Resets to <see cref="MinSnapshot"/>. Useful between tests.</summary>
    public void Reset() => _current = MinSnapshot;
}
