namespace NcaafPickEm.Simulator.Engine;

/// <summary>How a quick slate is shaped.</summary>
/// <param name="GameCount">Games in the slate, specials included (4..40).</param>
/// <param name="SlotMinutes">Minutes between kickoff windows.</param>
/// <param name="GamesPerSlot">Games that share a kickoff window.</param>
/// <param name="IncludeFcsGame">One FBS-vs-FCS game, which set generation must leave out.</param>
/// <param name="IncludeOffDayGame">One Friday-style game, which set generation must leave out.</param>
public sealed record SlateOptions(
    int GameCount = 14,
    int SlotMinutes = 10,
    int GamesPerSlot = 3,
    bool IncludeFcsGame = true,
    bool IncludeOffDayGame = true)
{
    /// <summary>The smallest slate worth building.</summary>
    public const int MinGames = 4;

    /// <summary>Well under the 50-game set maximum even if every game matched a rule.</summary>
    public const int MaxGames = 40;

    /// <summary>The same options with every number pulled into range.</summary>
    public SlateOptions Normalized() => this with
    {
        GameCount = Math.Clamp(GameCount, MinGames, MaxGames),
        SlotMinutes = Math.Clamp(SlotMinutes, 1, 24 * 60),
        GamesPerSlot = Math.Clamp(GamesPerSlot, 1, MaxGames),
    };
}
