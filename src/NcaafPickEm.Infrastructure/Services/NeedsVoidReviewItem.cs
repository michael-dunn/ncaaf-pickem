using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// One row of <see cref="GameSetService.ListNeedsVoidReviewAsync"/> (Feature 02/06, P3-04): a
/// game inside an already-locked week that has since been postponed or cancelled and has not yet
/// been voided. P2-04's data-status page lists these; P5-02's corrections flow is the only thing
/// that can resolve one (override the result or void it).
/// </summary>
public sealed record NeedsVoidReviewItem(
    Guid LeagueId,
    string LeagueName,
    int Week,
    Guid WeekGameSetId,
    Guid GameSetGameId,
    Guid GameId,
    string HomeTeam,
    string AwayTeam,
    GameStatus Status,
    DateTimeOffset KickoffUtc);
