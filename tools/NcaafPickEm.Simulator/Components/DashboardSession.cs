using NcaafPickEm.Simulator.Engine;
using NcaafPickEm.Simulator.Engine.Views;

namespace NcaafPickEm.Simulator.Components;

/// <summary>
/// One browser tab's view of the simulator (scoped to the Blazor circuit): the latest snapshot, the
/// team list for pickers, whether an action is running, and its result. Ticks once a second so
/// countdowns move, re-reads the snapshot every few seconds so changes made in the app (a pick, the
/// app's own lock job) show up, and re-reads at once after any action or autoplay move.
/// </summary>
public sealed class DashboardSession : IAsyncDisposable
{
    private const int RefreshEveryTicks = 3;

    private readonly SimWorld _world;
    private readonly SimActivity _activity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DashboardSession> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private Task? _loop;

    /// <summary>Creates the session.</summary>
    public DashboardSession(SimWorld world, SimActivity activity, TimeProvider timeProvider, ILogger<DashboardSession> logger)
    {
        _world = world;
        _activity = activity;
        _timeProvider = timeProvider;
        _logger = logger;
        _activity.Changed += OnActivity;
    }

    /// <summary>Raised whenever anything on screen may have changed; components re-render on it.</summary>
    public event Action? Changed;

    /// <summary>The world, for actions.</summary>
    public SimWorld World => _world;

    /// <summary>Real now, for countdowns.</summary>
    public DateTimeOffset NowUtc => _timeProvider.GetUtcNow();

    /// <summary>The latest snapshot, or null before the first read.</summary>
    public SimSnapshot? Snapshot { get; private set; }

    /// <summary>Every team, for pickers.</summary>
    public IReadOnlyList<SimTeamOption> Teams { get; private set; } = [];

    /// <summary>True while an action runs; buttons disable.</summary>
    public bool Busy { get; private set; }

    /// <summary>The last action this tab ran.</summary>
    public SimLog? LastLog { get; private set; }

    /// <summary>The activity feed, newest first.</summary>
    public IReadOnlyList<SimLog> Activity => _activity.Entries;

    /// <summary>Loads the first snapshot and starts ticking. Idempotent.</summary>
    public async Task StartAsync()
    {
        if (_loop is not null)
        {
            return;
        }

        await RefreshAsync(reloadTeams: true).ConfigureAwait(false);
        _loop = TickAsync(_stopping.Token);
    }

    /// <summary>Runs an action, then refreshes.</summary>
    public async Task RunAsync(Func<SimWorld, CancellationToken, Task<SimLog>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Busy)
        {
            return;
        }

        Busy = true;
        Changed?.Invoke();
        try
        {
            LastLog = await action(_world, _stopping.Token).ConfigureAwait(false);
        }
        finally
        {
            Busy = false;
        }

        await RefreshAsync(reloadTeams: Teams.Count == 0).ConfigureAwait(false);
    }

    /// <summary>Re-reads the snapshot (skipped if a read is already in flight).</summary>
    public async Task RefreshAsync(bool reloadTeams = false)
    {
        if (!await _refreshing.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            Snapshot = await _world.ReadAsync(_stopping.Token).ConfigureAwait(false);
            if (reloadTeams || Teams.Count == 0)
            {
                Teams = await _world.ReadTeamsAsync(_stopping.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A read racing a wipe or a migration: the next tick reads again.
            _logger.LogDebug(exception, "Dashboard refresh failed; retrying on the next tick");
        }
        finally
        {
            _refreshing.Release();
        }

        Changed?.Invoke();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _activity.Changed -= OnActivity;
        await _stopping.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopping.
            }
        }

        _stopping.Dispose();
        _refreshing.Dispose();
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _timeProvider);
        int tick = 0;

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (++tick % RefreshEveryTicks == 0)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
            else
            {
                Changed?.Invoke();
            }
        }
    }

    private void OnActivity() => _ = RefreshAsync();
}
