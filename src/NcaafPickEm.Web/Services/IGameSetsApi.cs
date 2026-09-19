using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Typed client for the game set configuration routes in 03-API-Contracts.md ("Game set
/// configuration", Feature 02), plus the reference-data lookups those pages need
/// (<c>/api/reference/conferences</c>, <c>/api/reference/teams</c>).
/// </summary>
public interface IGameSetsApi
{
    /// <summary><c>GET /api/leagues/{leagueId}/gameset-rules</c>: the league's default rules.</summary>
    Task<GameSetRuleDto[]> GetDefaultRulesAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary><c>PUT /api/leagues/{leagueId}/gameset-rules</c>: full replace of the default rules.</summary>
    Task<GameSetRuleDto[]> SaveDefaultRulesAsync(
        Guid leagueId, GameSetRuleDto[] rules, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}/weeks/{week}/gameset-rules</c>.</summary>
    Task<WeekRulesResponse> GetWeekRulesAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>PUT /api/leagues/{leagueId}/weeks/{week}/gameset-rules</c>. Passing
    /// <c>UsesOverride=false</c> clears the override and reverts the week to the default rules.
    /// </summary>
    Task<WeekRulesResponse> SaveWeekRulesAsync(
        Guid leagueId, int week, WeekRulesResponse request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/preview</c>: preview what the given
    /// (possibly unsaved) candidate rules would select.
    /// </summary>
    Task<GameSetPreview> PreviewAsync(
        Guid leagueId, int week, GameSetRuleDto[] candidateRules, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/generate</c>: regenerates the week's
    /// game set from its saved rules. Throws <see cref="LeaguesApiException"/> with
    /// <c>StatusCode == 409</c> when the week is locked or the selection exceeds 50 games.
    /// </summary>
    Task<WeekGameSetResponse> GenerateAsync(Guid leagueId, int week, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/games</c>: manually add one game.
    /// </summary>
    Task<WeekGameSetResponse> AddGameAsync(
        Guid leagueId, int week, Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>DELETE /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}</c>: manually remove
    /// one game.
    /// </summary>
    Task<WeekGameSetResponse> RemoveGameAsync(
        Guid leagueId, int week, Guid gameId, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/leagues/{leagueId}/weeks/{week}/gameset</c>.</summary>
    Task<WeekGameSetResponse> GetWeekGameSetAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/seasons/{year}/weeks/{week}/games?search=</c>: Saturday FBS candidates for
    /// manual add.
    /// </summary>
    Task<GameCandidate[]> SearchCandidatesAsync(
        int seasonYear, int week, string? search, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/reference/conferences</c>.</summary>
    Task<ConferenceDto[]> GetConferencesAsync(CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/reference/teams?search=</c>.</summary>
    Task<TeamDto[]> SearchTeamsAsync(string? search, CancellationToken cancellationToken = default);
}
