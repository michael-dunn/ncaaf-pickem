namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>One calendar week.</summary>
/// <param name="Week">Week number.</param>
/// <param name="StartUtc">Window start.</param>
/// <param name="EndUtc">Window end, inclusive.</param>
/// <param name="IsRegularSeason">False for championship week.</param>
/// <param name="GameCount">Simulated games scheduled in it.</param>
public sealed record SimWeekView(int Week, DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool IsRegularSeason, int GameCount);
