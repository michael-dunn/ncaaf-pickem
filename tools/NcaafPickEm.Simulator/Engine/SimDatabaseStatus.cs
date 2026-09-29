namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Whether the simulator may touch the database it is pointed at (D-186). Singleton: set once by
/// <see cref="SimDatabaseGuard"/> at startup, read before every operation and by the dashboard.
/// </summary>
public sealed class SimDatabaseStatus
{
    private volatile string? _problem = "Checking the database...";

    /// <summary>True when the database is the simulator's own and every action may run.</summary>
    public bool IsUsable => _problem is null;

    /// <summary>Why the simulator is refusing to work, or null when it is not.</summary>
    public string? Problem => _problem;

    /// <summary>The name of the database, for the dashboard header.</summary>
    public string DatabaseName { get; private set; } = string.Empty;

    /// <summary>Marks the database usable.</summary>
    /// <param name="databaseName">The database's name.</param>
    public void Accept(string databaseName)
    {
        DatabaseName = databaseName;
        _problem = null;
    }

    /// <summary>Marks the database off limits.</summary>
    /// <param name="databaseName">The database's name.</param>
    /// <param name="problem">What the dashboard should say.</param>
    public void Refuse(string databaseName, string problem)
    {
        DatabaseName = databaseName;
        _problem = problem;
    }
}
