using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IDevToolsApi"/> (P10-01): a clock that really moves and a demo week that
/// walks NotStarted -&gt; generated -&gt; picked -&gt; locked -&gt; scored as the controls are
/// pressed, so the Dev tools page can be built and screenshotted without a running Api.
/// </summary>
public sealed class FakeDevToolsApi : IDevToolsApi
{
    private static readonly DateTimeOffset FixtureWednesday = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixtureLockAt = new(2026, 10, 16, 23, 30, 0, TimeSpan.Zero);
    private static readonly string[] Names = ["Michael", "Alyson", "Dance", "Alex", "Daniel"];

    private TimeSpan _offset;
    private DateTimeOffset? _frozenAt;
    private int _snapshot = 1;
    private bool _generated;
    private bool _picked;
    private bool _locked;
    private bool _scored;

    /// <inheritdoc />
    public Task<DevClockResponse> GetClockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Clock());

    /// <inheritdoc />
    public Task<DevClockResponse> SetClockAsync(DevClockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Same order as DevClockEndpoints: a true Frozen applies first, a false one last.
        if (request.Frozen is true)
        {
            _frozenAt ??= Now();
        }

        if (request.NowUtc is DateTimeOffset nowUtc)
        {
            if (_frozenAt is not null)
            {
                _frozenAt = nowUtc;
            }
            else
            {
                _offset = nowUtc - DateTimeOffset.UtcNow;
            }
        }

        if (request.Advance is TimeSpan advance)
        {
            if (_frozenAt is DateTimeOffset frozen)
            {
                _frozenAt = frozen + advance;
            }
            else
            {
                _offset += advance;
            }
        }

        if (request.Frozen is false && _frozenAt is DateTimeOffset thawFrom)
        {
            _offset = thawFrom - DateTimeOffset.UtcNow;
            _frozenAt = null;
        }

        return Task.FromResult(Clock());
    }

    /// <inheritdoc />
    public Task<DevClockResponse> ResetClockAsync(CancellationToken cancellationToken = default)
    {
        _offset = TimeSpan.Zero;
        _frozenAt = null;
        return Task.FromResult(Clock());
    }

    /// <inheritdoc />
    public Task<DemoWeekResponse> GetDemoWeekAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Demo([]));

    /// <inheritdoc />
    public Task<DemoWeekResponse> GenerateDemoWeekAsync(CancellationToken cancellationToken = default)
    {
        if (_locked)
        {
            return Task.FromResult(Demo(["generate: skipped (Locked: the week is locked)"]));
        }

        _generated = true;
        return Task.FromResult(Demo(["generate: 12 game(s) in the week 7 set"]));
    }

    /// <inheritdoc />
    public Task<DemoWeekResponse> FillDemoPicksAsync(bool includeMe, CancellationToken cancellationToken = default)
    {
        if (!_generated)
        {
            return Task.FromResult(Demo(["picks: the week has no active games; generate the set first"]));
        }

        if (_locked)
        {
            return Task.FromResult(Demo(["picks: Alyson stopped (Locked: Picks for this week are locked.)"]));
        }

        _picked = true;
        string[] notes =
        [
            includeMe ? "picks: Michael made 12 pick(s), status Submitted" : "picks: Michael skipped (that is you)",
            "picks: Alyson made 12 pick(s), status Submitted",
            "picks: Dance made 12 pick(s), status Submitted",
            "picks: Alex made 12 pick(s), status Submitted",
            "picks: Daniel made 12 pick(s), status Submitted",
        ];
        return Task.FromResult(Demo(notes));
    }

    /// <inheritdoc />
    public Task<DemoWeekResponse> LockDemoWeekAsync(CancellationToken cancellationToken = default)
    {
        if (!_generated)
        {
            return Task.FromResult(Demo(["lock: nothing to lock (no set, or the set has no active games)"]));
        }

        if (_locked)
        {
            return Task.FromResult(Demo([$"lock: already locked at {FixtureLockAt:o}"]));
        }

        _locked = true;
        return Task.FromResult(Demo(["lock: LockWeekJob ran; picks are frozen and point values are settled"]));
    }

    /// <inheritdoc />
    public Task<DemoWeekResponse> PollDemoWeekAsync(int? snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is int requested)
        {
            _snapshot = Math.Clamp(requested, 1, 6);
        }

        _scored = _locked && _snapshot >= 2;
        return Task.FromResult(Demo([$"poll: snapshot {_snapshot} for 2026-10-17 (Eastern): 14 matched, 12 changed, 0 unmatched, 3 event(s)"]));
    }

    /// <inheritdoc />
    public Task<DemoWeekResponse> ResetDemoWeekAsync(CancellationToken cancellationToken = default)
    {
        _generated = false;
        _picked = false;
        _locked = false;
        _scored = false;
        _snapshot = 1;
        return Task.FromResult(Demo(["reset: deleted 1 set(s), 12 set game(s), 60 pick(s), 5 submission(s), 5 result(s), 5 standings row(s); 16 fixture game(s) back to Scheduled; snapshot 1"]));
    }

    private DateTimeOffset Now() => _frozenAt ?? DateTimeOffset.UtcNow + _offset;

    private DevClockResponse Clock()
    {
        DateTimeOffset now = Now();
        bool inFixtureWeek = now >= FixtureWednesday.AddDays(-3) && now <= FixtureWednesday.AddDays(4);
        return new DevClockResponse(
            now,
            DateTimeOffset.UtcNow,
            now - DateTimeOffset.UtcNow,
            _frozenAt is not null,
            _frozenAt is not null || _offset != TimeSpan.Zero,
            $"{now.ToOffset(TimeSpan.FromHours(-4)):ddd h:mm tt} ET",
            inFixtureWeek ? 7 : 3,
            "InSeason");
    }

    private DemoWeekResponse Demo(string[] notes)
    {
        DateTimeOffset now = Now();
        bool allFinal = _snapshot == 6;

        DemoWeekSetDto? set = _generated
            ? new DemoWeekSetDto(
                7,
                12,
                12,
                _scored ? (allFinal ? 12 : 4) : 0,
                _scored && !allFinal ? 5 : 0,
                FixtureLockAt,
                "Fri 7:30 PM ET",
                _locked ? FixtureLockAt : null,
                _locked || now >= FixtureLockAt,
                _scored && allFinal)
            : null;

        DemoMemberDto[] members =
        [
            .. Names.Select((name, index) => new DemoMemberDto(
                name,
                index == 0 ? "Commissioner" : "Member",
                _locked ? (_picked ? "Locked" : "Incomplete") : _picked ? "Submitted" : "NotStarted",
                _picked ? 12 : 0,
                _scored ? 80 - (index * 10) : null,
                _scored ? 8 - index : null))
        ];

        return new DemoWeekResponse(
            FakeLeaguesApi.SampleLeagueId,
            "Family League",
            now,
            7,
            "InSeason",
            true,
            _snapshot,
            set,
            members,
            notes);
    }
}
