using Microsoft.AspNetCore.Components;
using NcaafPickEm.Simulator.Engine;
using NcaafPickEm.Simulator.Engine.Views;

namespace NcaafPickEm.Simulator.Components;

/// <summary>
/// Base for every dashboard part: the tab's <see cref="DashboardSession"/>, re-rendering whenever
/// it changes, and a one-line way to run an action.
/// </summary>
public abstract class SessionComponent : ComponentBase, IDisposable
{
    /// <summary>The tab's session.</summary>
    [Inject]
    protected DashboardSession Session { get; set; } = default!;

    /// <summary>The latest snapshot, or null before the first read.</summary>
    protected SimSnapshot? Snapshot => Session.Snapshot;

    /// <summary>True while an action runs.</summary>
    protected bool Busy => Session.Busy;

    /// <summary>Real now.</summary>
    protected DateTimeOffset Now => Session.NowUtc;

    /// <summary>The current week, or null with no calendar.</summary>
    protected int? Week => Snapshot?.Calendar.Current?.Week;

    /// <inheritdoc />
    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    /// <summary>Runs an action through the session.</summary>
    protected Task Run(Func<SimWorld, CancellationToken, Task<SimLog>> action) => Session.RunAsync(action);

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);
}
