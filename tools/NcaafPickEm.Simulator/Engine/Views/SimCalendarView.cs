using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Simulator.Engine.Views;

/// <summary>The simulated season's calendar as the app sees it.</summary>
/// <param name="Weeks">Every week, in order; empty before the first layout.</param>
/// <param name="Current">The app's current week and season state, or null with no calendar.</param>
public sealed record SimCalendarView(IReadOnlyList<SimWeekView> Weeks, CurrentWeek? Current)
{
    /// <summary>The current week's row, or null.</summary>
    public SimWeekView? CurrentWeekRow =>
        Current is CurrentWeek current ? Weeks.FirstOrDefault(week => week.Week == current.Week) : null;
}
