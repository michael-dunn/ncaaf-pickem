using NcaafPickEm.Shared.Contracts.Auth;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for <c>/api/me</c> (Feature 08 Profile, P1-03). Pages depend on this interface,
/// never on <see cref="HttpClient"/> directly, matching <see cref="ILeaguesApi"/>'s pattern.
/// </summary>
public interface IMeApi
{
    /// <summary><c>GET /api/me</c>.</summary>
    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>PUT /api/me</c>. 409 (as <see cref="LeaguesApiException"/>) when the new global name
    /// would collide with another active member's effective name in a league where the caller
    /// has no per-league override.
    /// </summary>
    Task<MeResponse> UpdateDisplayNameAsync(UpdateMeRequest request, CancellationToken cancellationToken = default);
}
