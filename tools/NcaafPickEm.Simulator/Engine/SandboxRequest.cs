namespace NcaafPickEm.Simulator.Engine;

/// <summary>Everything "create a sandbox" needs; the defaults give a ready-to-play league in one click.</summary>
/// <param name="LeagueName">The new league.</param>
/// <param name="TesterName">"You": the commissioner, and the player the fill buttons leave alone.</param>
/// <param name="PlayerNames">Everyone else, joined through an invite.</param>
/// <param name="StartWeek">The week to start on when the calendar is not already in season.</param>
/// <param name="Conference">The conference whose conference games the league's second rule adds.</param>
/// <param name="LockInMinutes">Minutes from now to the first kickoff.</param>
/// <param name="GameMinutes">Real minutes per game under autoplay.</param>
/// <param name="AutoPlay">Play games out on their own.</param>
/// <param name="Slate">How the week's slate is shaped.</param>
public sealed record SandboxRequest(
    string LeagueName = "Sim League",
    string TesterName = "Michael",
    IReadOnlyList<string>? PlayerNames = null,
    int StartWeek = 1,
    string Conference = "SEC",
    int LockInMinutes = 15,
    int GameMinutes = SimState.DefaultGameMinutes,
    bool AutoPlay = true,
    SlateOptions? Slate = null)
{
    /// <summary>The Overview's worked-example family, plus one who never picks on time.</summary>
    public static readonly IReadOnlyList<string> DefaultPlayers = ["Alyson", "Dance", "Alex", "Daniel", "Sam"];

    /// <summary>The players to add, defaulted.</summary>
    public IReadOnlyList<string> Players => PlayerNames ?? DefaultPlayers;
}
