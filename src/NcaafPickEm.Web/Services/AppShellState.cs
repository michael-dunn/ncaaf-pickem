namespace NcaafPickEm.Web.Services;

/// <summary>
/// The small amount of state the app shell (top bar and bottom tab bar) needs from the page
/// currently on screen: what to call the screen, and which league and week are in context.
/// </summary>
/// <remarks>
/// Registered scoped and cascaded by <c>MainLayout</c>. A page sets it from
/// <c>OnParametersSet</c>; the layout re-renders through <see cref="Changed"/>. Pages that are
/// not inside a league leave <see cref="LeagueId"/> null, which collapses the tab bar links
/// back to the home route.
/// </remarks>
public sealed class AppShellState
{
    /// <summary>The product name, shown in the top bar whenever no league is in context.</summary>
    public const string AppName = "NCAAF Pick Em";

    private string _title = AppName;
    private string? _leagueName;
    private Guid? _leagueId;
    private int? _week;

    /// <summary>Raised whenever any shell-visible value changes.</summary>
    public event Action? Changed;

    /// <summary>Title for the current screen. Used as the top bar's secondary line.</summary>
    public string Title => _title;

    /// <summary>League currently in context, or null outside a league.</summary>
    public Guid? LeagueId => _leagueId;

    /// <summary>Name of the league in context; the top bar's primary line when set.</summary>
    public string? LeagueName => _leagueName;

    /// <summary>Week currently in context, used to build the Picks and Dashboard tab routes.</summary>
    public int? Week => _week;

    /// <summary>The text the top bar shows as its primary line.</summary>
    public string HeaderText => _leagueName ?? AppName;

    /// <summary>Sets the current screen title.</summary>
    /// <param name="title">Screen title; blank falls back to the app name.</param>
    public void SetTitle(string? title)
    {
        string resolved = string.IsNullOrWhiteSpace(title) ? AppName : title.Trim();
        if (_title == resolved)
        {
            return;
        }

        _title = resolved;
        Changed?.Invoke();
    }

    /// <summary>Puts a league (and optionally a week) into context for the shell.</summary>
    /// <param name="leagueId">League identifier.</param>
    /// <param name="leagueName">League display name.</param>
    /// <param name="week">Week in context, if the screen has one.</param>
    public void SetLeague(Guid leagueId, string? leagueName, int? week = null)
    {
        if (_leagueId == leagueId && _leagueName == leagueName && _week == week)
        {
            return;
        }

        _leagueId = leagueId;
        _leagueName = leagueName;
        _week = week;
        Changed?.Invoke();
    }

    /// <summary>Updates only the week in context.</summary>
    /// <param name="week">Week in context, or null.</param>
    public void SetWeek(int? week)
    {
        if (_week == week)
        {
            return;
        }

        _week = week;
        Changed?.Invoke();
    }

    /// <summary>Drops the league context, for screens outside any league.</summary>
    public void ClearLeague()
    {
        if (_leagueId is null && _leagueName is null && _week is null)
        {
            return;
        }

        _leagueId = null;
        _leagueName = null;
        _week = null;
        Changed?.Invoke();
    }
}
