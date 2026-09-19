using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the member-facing picks routes in 03-API-Contracts.md ("Picks", Feature 04).
/// The commissioner-only <c>picks</c>/<c>picks/status</c> routes are not exposed here; this
/// interface is only what <c>Pages/Picks/PicksPage.razor</c> (P4-03) needs.
/// </summary>
public interface IPicksApi
{
    /// <summary>
    /// <c>GET /api/leagues/{leagueId}/weeks/{week}/picks/me</c>. Works read-only for any week in
    /// the league's range, so past weeks render with results.
    /// </summary>
    Task<MyPicksResponse> GetMyPicksAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>PUT /api/leagues/{leagueId}/weeks/{week}/picks/me/{gameId}</c>. Throws
    /// <see cref="LeaguesApiException"/> (409 <c>Locked</c>/<c>WeekNotCurrent</c>/<c>GameNotActive</c>,
    /// 400 <c>TeamNotInGame</c>) on failure; re-picking the same team is a 200 no-op.
    /// </summary>
    Task<MyPicksResponse> SetPickAsync(
        Guid leagueId, int week, Guid gameId, Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/picks/me/submit</c>. Throws
    /// <see cref="LeaguesApiException"/> with <c>StatusCode == 409</c> and <c>Count</c> set for
    /// <c>IncompletePicks</c>.
    /// </summary>
    Task<MyPicksResponse> SubmitAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/picks/me/ack-changes</c>. Clears
    /// <see cref="MyPicksResponse.HasUnseenGameChanges"/>; 204 either way, so this never throws for
    /// "no submission row yet".
    /// </summary>
    Task AckChangesAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);
}
