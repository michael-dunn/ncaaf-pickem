using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IAdminApi"/>: a plausible mid-Saturday data status (one stale slice, two
/// unmatched games, one needs-review tie, the CFBD counter near its warning threshold), so the
/// Data status page (P2-04) can be built and screenshotted without a running Api.
/// </summary>
/// <param name="store">
/// Shared game set state (P5-05): the needs-review row's <c>GameId</c> is taken from here so the
/// Data status page's Void action operates on the same in-memory game the week view shows.
/// </param>
public sealed class FakeAdminApi(FakeGameSetStore store) : IAdminApi
{
    private static readonly Guid SampleLeagueId = FakeLeaguesApi.SampleLeagueId;

    private readonly FakeGameSetStore _store = store;

    private readonly List<UnmatchedGameDto> _unmatched =
    [
        new(
            Guid.NewGuid(),
            ProviderSource.Espn,
            "Wildcats",
            "Golden Eagles",
            new DateOnly(2026, 10, 17),
            DateTimeOffset.UtcNow.AddHours(-2)),
        new(
            Guid.NewGuid(),
            ProviderSource.Espn,
            "Blue Raiders",
            "Owls",
            new DateOnly(2026, 10, 17),
            DateTimeOffset.UtcNow.AddHours(-1)),
    ];

    /// <inheritdoc />
    public Task<DataStatusResponse> GetDataStatusAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        RefreshStatusDto[] refreshes =
        [
            new(RefreshDataType.Teams, now.AddDays(-2), now.AddDays(-2), null),
            new(RefreshDataType.Schedule, now.AddHours(-20), now.AddHours(-20), null),
            new(RefreshDataType.Rankings, now.AddDays(-1), now.AddDays(-1), null),
            new(RefreshDataType.Lines, now.AddHours(-10), now.AddHours(-10), null),
            new(
                RefreshDataType.Scores,
                now.AddDays(-7),
                now.AddMinutes(-5),
                "ESPN timed out after 30s (falling back to CFBD)"),
        ];

        NeedsReviewGameDto[] needsReview = _store.IsVoided(_store.NeedsReviewDemoGameId)
            ? []
            :
            [
                new(
                    _store.NeedsReviewDemoGameId,
                    SampleLeagueId,
                    "The Family League",
                    7,
                    "Iowa State",
                    "Kansas",
                    24,
                    24,
                    "Tie"),
            ];

        JobRunDto[] recentJobs =
        [
            new(Guid.NewGuid(), "ScheduleRefresh", now.AddHours(-20), now.AddHours(-20), now.AddHours(-20).AddSeconds(4), true, null),
            new(Guid.NewGuid(), "LinesRefresh", now.AddHours(-10), now.AddHours(-10), now.AddHours(-10).AddSeconds(2), true, null),
            new(Guid.NewGuid(), "Heartbeat", now.AddMinutes(-5), now.AddMinutes(-5), now.AddMinutes(-5).AddSeconds(1), true, null),
        ];

        return Task.FromResult(new DataStatusResponse(
            refreshes,
            812,
            true,
            "Espn",
            [.. _unmatched],
            recentJobs,
            "Cfbd",
            true,
            needsReview));
    }

    /// <inheritdoc />
    public Task<ManualRefreshResponse> RefreshAsync(
        RefreshDataType dataType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ManualRefreshResponse(dataType, true, null));

    /// <inheritdoc />
    public Task ResolveUnmatchedAsync(
        Guid id,
        ResolveUnmatchedRequest request,
        CancellationToken cancellationToken = default)
    {
        _unmatched.RemoveAll(row => row.Id == id);
        return Task.CompletedTask;
    }
}
