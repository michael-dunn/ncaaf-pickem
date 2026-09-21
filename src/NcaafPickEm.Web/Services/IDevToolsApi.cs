using NcaafPickEm.Shared.Contracts.Dev;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the Development-only <c>/api/admin/fixture/*</c> tools (P10-01): the server
/// clock, the fixture score snapshot, and the demo-league week controls. Every call throws
/// <see cref="LeaguesApiException"/> with <c>StatusCode == 404</c> against a Production server,
/// where none of these routes exist; the Dev tools page treats that as "not available here".
/// </summary>
public interface IDevToolsApi
{
    /// <summary><c>GET /api/admin/fixture/clock</c>.</summary>
    Task<DevClockResponse> GetClockAsync(CancellationToken cancellationToken = default);

    /// <summary><c>PUT /api/admin/fixture/clock</c>: move, advance and/or freeze the server clock.</summary>
    Task<DevClockResponse> SetClockAsync(DevClockRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>DELETE /api/admin/fixture/clock</c>: back to real time.</summary>
    Task<DevClockResponse> ResetClockAsync(CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/admin/fixture/demo</c>: where the demo league stands this week.</summary>
    Task<DemoWeekResponse> GetDemoWeekAsync(CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/fixture/demo/generate</c>.</summary>
    Task<DemoWeekResponse> GenerateDemoWeekAsync(CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/fixture/demo/picks?includeMe=</c>.</summary>
    Task<DemoWeekResponse> FillDemoPicksAsync(bool includeMe, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/fixture/demo/lock</c>.</summary>
    Task<DemoWeekResponse> LockDemoWeekAsync(CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/fixture/demo/poll?snapshot=</c>.</summary>
    Task<DemoWeekResponse> PollDemoWeekAsync(int? snapshot, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/fixture/demo/reset</c>.</summary>
    Task<DemoWeekResponse> ResetDemoWeekAsync(CancellationToken cancellationToken = default);
}
