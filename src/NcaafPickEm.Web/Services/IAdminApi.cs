using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for <c>/api/admin/*</c> (Features 09, 12; P2-04). Pages depend on this interface,
/// never on <see cref="HttpClient"/> directly, matching <see cref="ILeaguesApi"/>'s pattern.
/// </summary>
public interface IAdminApi
{
    /// <summary>
    /// <c>GET /api/admin/data-status</c>. Throws <see cref="LeaguesApiException"/> with
    /// <c>StatusCode == 403</c> when the caller commissions nothing.
    /// </summary>
    Task<DataStatusResponse> GetDataStatusAsync(CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/refresh/{dataType}</c>.</summary>
    Task<ManualRefreshResponse> RefreshAsync(RefreshDataType dataType, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/admin/unmatched/{id}/resolve</c>.</summary>
    Task ResolveUnmatchedAsync(Guid id, ResolveUnmatchedRequest request, CancellationToken cancellationToken = default);
}
