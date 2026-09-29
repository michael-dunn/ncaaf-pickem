namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>The simulator's knobs.</summary>
/// <param name="AutoPlay">Autoplay on.</param>
/// <param name="GameMinutes">Real minutes per game under autoplay.</param>
/// <param name="LockLeadMinutes">Minutes from now to the first kickoff of a new week.</param>
/// <param name="Slate">How new slates are shaped.</param>
public sealed record SimSettingsView(bool AutoPlay, int GameMinutes, int LockLeadMinutes, SlateOptions Slate);
