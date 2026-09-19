namespace NcaafPickEm.Shared.Contracts.Picks;

/// <summary>Body of <c>PUT /api/leagues/{leagueId}/weeks/{week}/picks/me/{gameId}</c>.</summary>
/// <param name="TeamId">The team being picked; must be the home or away team of that game.</param>
public sealed record SetPickRequest(Guid TeamId);
