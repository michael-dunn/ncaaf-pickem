using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the "Scoring and corrections" routes in 03-API-Contracts.md (Feature 06,
/// P5-02): commissioner overrides and voids after lock, plus the member-visible audit log.
/// </summary>
public interface ICorrectionsApi
{
    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/override-result</c>.
    /// Commissioner-only, after lock. Returns the updated <see cref="GameSetGameDto"/> for that
    /// game (same shape as the point-override route, D-180). Throws
    /// <see cref="LeaguesApiException"/> with <c>StatusCode == 409</c> (title <c>NotLocked</c> or
    /// <c>AlreadyVoided</c>) or <c>StatusCode == 400</c> (title <c>TeamNotInGame</c>).
    /// </summary>
    Task<GameSetGameDto> OverrideResultAsync(
        Guid leagueId, int week, Guid gameId, Guid winnerTeamId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/void</c>.
    /// Commissioner-only, after lock. Returns the updated <see cref="GameSetGameDto"/>. Throws
    /// <see cref="LeaguesApiException"/> with <c>StatusCode == 409</c> (title <c>NotLocked</c> or
    /// <c>AlreadyVoided</c>).
    /// </summary>
    Task<GameSetGameDto> VoidGameAsync(
        Guid leagueId, int week, Guid gameId, string reason, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}/audit</c>. Visible to every member, newest first.</summary>
    Task<AuditEntry[]> GetAuditAsync(Guid leagueId, CancellationToken cancellationToken = default);
}
