namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>
/// Body of <c>GET</c>/<c>PUT /api/leagues/{leagueId}/weeks/{week}/gameset-rules</c>. When
/// <paramref name="UsesOverride"/> is false the week uses the league's default rules and
/// <paramref name="Rules"/> echoes them read-only.
/// </summary>
/// <param name="UsesOverride">True when this week has its own rule set.</param>
/// <param name="Rules">The rules in effect for the week.</param>
public sealed record WeekRulesResponse(
    bool UsesOverride,
    GameSetRuleDto[] Rules);
