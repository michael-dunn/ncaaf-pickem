namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// What one simulator action did, line by line. Refusals from the app's own rules (a pick after
/// the lock, a regenerate on a frozen week) are lines, not failures: seeing where the week stands
/// is the point.
/// </summary>
public sealed class SimLog
{
    private readonly List<string> _lines = [];

    /// <summary>Starts the log for an action.</summary>
    /// <param name="title">The action, e.g. "Lock now".</param>
    /// <param name="atUtc">When it ran.</param>
    public SimLog(string title, DateTimeOffset atUtc)
    {
        Title = title;
        AtUtc = atUtc;
    }

    /// <summary>The action.</summary>
    public string Title { get; }

    /// <summary>When it ran.</summary>
    public DateTimeOffset AtUtc { get; }

    /// <summary>What happened, in order.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>True when the action stopped on an error.</summary>
    public bool Failed { get; private set; }

    /// <summary>Adds a line.</summary>
    public void Note(string line) => _lines.Add(line);

    /// <summary>Adds the line that stopped the action.</summary>
    public void Fail(string line)
    {
        Failed = true;
        _lines.Add(line);
    }
}
