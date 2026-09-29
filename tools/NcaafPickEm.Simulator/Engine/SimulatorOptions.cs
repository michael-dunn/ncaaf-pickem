namespace NcaafPickEm.Simulator.Engine;

/// <summary>The simulator's own configuration section, <c>Simulator</c>.</summary>
public sealed class SimulatorOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Simulator";

    /// <summary>Where the app runs, for the "open the app as..." links.</summary>
    public string AppBaseUrl { get; set; } = "https://localhost:7092";

    /// <summary>The season to simulate when the database has no state yet; 0 means this year.</summary>
    public int SeasonYear { get; set; }

    /// <summary>How often autoplay moves the games along.</summary>
    public int AutoplayTickSeconds { get; set; } = 5;
}
