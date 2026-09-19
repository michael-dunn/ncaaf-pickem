using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the point value routes in 03-API-Contracts.md ("Point values", Feature 03).
/// </summary>
public interface IPointRulesApi
{
    /// <summary><c>GET /api/leagues/{leagueId}/point-rules</c>.</summary>
    Task<PointRuleDto[]> GetRulesAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>PUT /api/leagues/{leagueId}/point-rules</c>: full replace, order = priority.
    /// </summary>
    Task<PointRuleDto[]> SaveRulesAsync(
        Guid leagueId, PointRuleDto[] rules, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>PUT /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/points</c>. Null
    /// <paramref name="pointValue"/> clears the override. Throws <see cref="LeaguesApiException"/>
    /// with <c>StatusCode == 409</c> when the week is locked.
    /// </summary>
    Task<WeekGameSetResponse> SetOverrideAsync(
        Guid leagueId, int week, Guid gameId, int? pointValue, CancellationToken cancellationToken = default);
}
