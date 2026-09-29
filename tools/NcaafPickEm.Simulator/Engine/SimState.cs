namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Everything the simulator remembers that the app's tables do not hold: which season it is
/// simulating, who "you" are, autoplay, and each game's plan. Stored as one JSON document in
/// <c>sim.Settings</c>, in the same database as the data it describes (D-186).
/// </summary>
public sealed class SimState
{
    /// <summary>Default autoplay game length: a whole game in a coffee break.</summary>
    public const int DefaultGameMinutes = 12;

    /// <summary>The season every simulated league, game and calendar week belongs to.</summary>
    public int SeasonYear { get; set; }

    /// <summary>
    /// The tester's own user, whose picks "fill everyone else" leaves alone; null until a
    /// sandbox exists.
    /// </summary>
    public Guid? TesterUserId { get; set; }

    /// <summary>When true, the autoplay loop plays every started game out on its plan.</summary>
    public bool AutoPlay { get; set; } = true;

    /// <summary>Real minutes from kickoff to the final whistle under autoplay.</summary>
    public int GameMinutes { get; set; } = DefaultGameMinutes;

    /// <summary>Minutes from "now" to the first kickoff whenever the simulator lays out a new week.</summary>
    public int LockLeadMinutes { get; set; } = 15;

    /// <summary>How new slates are shaped.</summary>
    public SlateOptions Slate { get; set; } = new();

    /// <summary>Autoplay plans by <c>Games.Id</c>.</summary>
    public Dictionary<Guid, GamePlan> Plans { get; set; } = [];
}
