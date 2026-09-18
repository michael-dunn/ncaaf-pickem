namespace NcaafPickEm.Shared.Contracts.GameSets;

/// <summary>Body of <c>POST /api/leagues/{leagueId}/weeks/{week}/gameset/games</c> (manual add).</summary>
/// <param name="GameId">The <c>Games.Id</c> to add; must be a Saturday-Eastern FBS game in that week.</param>
public sealed record AddGameRequest(Guid GameId);
