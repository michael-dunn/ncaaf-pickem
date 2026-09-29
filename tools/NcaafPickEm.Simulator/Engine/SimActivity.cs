namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// The dashboard's activity feed: the last actions' logs, newest first, and a signal that
/// something changed so every open dashboard refreshes. Singleton.
/// </summary>
public sealed class SimActivity
{
    private const int Capacity = 200;

    private readonly Lock _gate = new();
    private readonly LinkedList<SimLog> _entries = new();

    /// <summary>Raised after every action and every autoplay tick that moved a game.</summary>
    public event Action? Changed;

    /// <summary>The recent logs, newest first.</summary>
    public IReadOnlyList<SimLog> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Records a log and tells the dashboards.</summary>
    public void Add(SimLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        lock (_gate)
        {
            _entries.AddFirst(log);
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Tells the dashboards to refresh without recording anything.</summary>
    public void Touch() => Changed?.Invoke();
}
