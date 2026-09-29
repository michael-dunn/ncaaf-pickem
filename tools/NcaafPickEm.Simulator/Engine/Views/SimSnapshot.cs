namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>Everything the dashboard shows, read in one go.</summary>
/// <param name="NowUtc">Real now - the only now there is.</param>
/// <param name="DatabaseName">The database the simulator (and the app) are pointed at.</param>
/// <param name="DatabaseProblem">Why the simulator refuses to work on it, or null.</param>
/// <param name="SeasonYear">The simulated season.</param>
/// <param name="Calendar">The season's weeks and which one is current.</param>
/// <param name="Settings">Autoplay and new-week defaults.</param>
/// <param name="TesterUserId">"You", whose picks the fill buttons leave alone.</param>
/// <param name="Games">The current week's games, in kickoff order.</param>
/// <param name="Poll">The current week's AP poll.</param>
/// <param name="Leagues">Every league in the season with its current-week set and members.</param>
public sealed record SimSnapshot(
    DateTimeOffset NowUtc,
    string DatabaseName,
    string? DatabaseProblem,
    int SeasonYear,
    SimCalendarView Calendar,
    SimSettingsView Settings,
    Guid? TesterUserId,
    IReadOnlyList<SimGameView> Games,
    IReadOnlyList<SimRankView> Poll,
    IReadOnlyList<SimLeagueView> Leagues)
{
    /// <summary>The earliest lock this week that has not happened yet, or null.</summary>
    public DateTimeOffset? NextLockUtc =>
        Leagues
            .Select(league => league.Set)
            .Where(set => set is { LockedUtc: null, LockAtUtc: not null })
            .Select(set => set!.LockAtUtc)
            .Min();
}
