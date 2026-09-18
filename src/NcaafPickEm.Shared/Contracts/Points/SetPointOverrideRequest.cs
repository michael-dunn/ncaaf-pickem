namespace NcaafPickEm.Shared.Contracts.Points;

/// <summary>Body of <c>PUT /api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/points</c>.</summary>
/// <param name="PointValue">Manual value 1..100, or null to clear the override and fall back to rules.</param>
public sealed record SetPointOverrideRequest(int? PointValue);
