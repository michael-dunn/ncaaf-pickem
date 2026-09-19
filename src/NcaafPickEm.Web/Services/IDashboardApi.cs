using NcaafPickEm.Shared.Contracts.Dashboard;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the influence dashboard route in 03-API-Contracts.md ("Influence dashboard",
/// Feature 05).
/// </summary>
public interface IDashboardApi
{
    /// <summary>
    /// <c>GET /api/leagues/{leagueId}/weeks/{week}/dashboard</c>. Before lock the response has
    /// <c>IsAvailable == false</c> and only the lock fields are meaningful.
    /// </summary>
    Task<DashboardResponse> GetDashboardAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);
}
