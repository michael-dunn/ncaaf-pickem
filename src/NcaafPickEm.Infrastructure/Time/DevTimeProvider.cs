namespace NcaafPickEm.Infrastructure.Time;

/// <summary>
/// The app's clock in Development and Testing: real time plus a movable offset, or one frozen
/// instant. Lets a running server be walked into the fixture week (P10-01) so picks, the lock,
/// the Saturday poller and scoring can all be exercised from a browser without waiting for
/// October.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="GetUtcNow"/> is overridden. Timers and timestamps stay real, so the
/// once-a-minute <c>JobScheduler</c> and the <c>SaturdayPoller</c> keep ticking at their normal
/// cadence and simply read a shifted "now" on each tick. A frozen clock therefore still lets the
/// scheduler run; it just never sees the minute change.
/// </para>
/// <para>
/// Never registered in Production (<c>AddInfrastructure</c> keeps <see cref="TimeProvider.System"/>
/// there). Safe under concurrent requests: every read and write takes the same lock.
/// </para>
/// </remarks>
public sealed class DevTimeProvider : TimeProvider
{
    private readonly TimeProvider _real;
    private readonly Lock _gate = new();
    private TimeSpan _offset;
    private DateTimeOffset? _frozenAt;

    /// <summary>A dev clock over the system clock, initially unshifted.</summary>
    public DevTimeProvider()
        : this(System)
    {
    }

    /// <summary>A dev clock over <paramref name="real"/>, initially unshifted. Tests pass a fake.</summary>
    /// <param name="real">The clock that supplies real time.</param>
    public DevTimeProvider(TimeProvider real)
    {
        ArgumentNullException.ThrowIfNull(real);
        _real = real;
    }

    /// <summary>What the app believes "now" is: the frozen instant, or real time plus the offset.</summary>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return NowUnlocked();
        }
    }

    /// <summary>Real time, unshifted.</summary>
    public DateTimeOffset RealUtcNow => _real.GetUtcNow();

    /// <summary>
    /// How far the app clock is from real time right now. For a frozen clock this grows more
    /// negative every real second, because the app is standing still.
    /// </summary>
    public TimeSpan Offset
    {
        get
        {
            lock (_gate)
            {
                return NowUnlocked() - _real.GetUtcNow();
            }
        }
    }

    /// <summary>True while the clock stands still.</summary>
    public bool IsFrozen
    {
        get
        {
            lock (_gate)
            {
                return _frozenAt is not null;
            }
        }
    }

    /// <summary>True when the clock is frozen or offset; false when it reads real time.</summary>
    public bool IsShifted
    {
        get
        {
            lock (_gate)
            {
                return _frozenAt is not null || _offset != TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// Moves the clock so that it reads <paramref name="nowUtc"/> now. A running clock keeps
    /// running from there; a frozen one stays frozen at the new instant.
    /// </summary>
    /// <param name="nowUtc">The instant the app should believe it is.</param>
    public void SetNow(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (_frozenAt is not null)
            {
                _frozenAt = nowUtc;
            }
            else
            {
                _offset = nowUtc - _real.GetUtcNow();
            }
        }
    }

    /// <summary>Moves the clock by <paramref name="by"/>, forwards or backwards.</summary>
    /// <param name="by">The amount to move; negative goes backwards.</param>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            if (_frozenAt is DateTimeOffset frozen)
            {
                _frozenAt = frozen + by;
            }
            else
            {
                _offset += by;
            }
        }
    }

    /// <summary>Stops the clock at its current reading. A no-op when already frozen.</summary>
    public void Freeze()
    {
        lock (_gate)
        {
            _frozenAt ??= NowUnlocked();
        }
    }

    /// <summary>
    /// Lets a frozen clock run again from its current reading, keeping the offset that reading
    /// implies. A no-op when the clock is already running.
    /// </summary>
    public void Thaw()
    {
        lock (_gate)
        {
            if (_frozenAt is DateTimeOffset frozen)
            {
                _offset = frozen - _real.GetUtcNow();
                _frozenAt = null;
            }
        }
    }

    /// <summary>Back to real time: no offset, not frozen.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _offset = TimeSpan.Zero;
            _frozenAt = null;
        }
    }

    private DateTimeOffset NowUnlocked() => _frozenAt ?? _real.GetUtcNow() + _offset;
}
