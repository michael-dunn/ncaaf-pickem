using NcaafPickEm.Domain.Events;

namespace NcaafPickEm.Domain.Scoring.Events;

/// <summary>
/// A commissioner voided one game in one league's locked week (Feature 06, "void a game"). Raised
/// by P5-02's void endpoint after <c>WeekGameSetGames.IsVoided</c> and the <c>AuditLog</c> row are
/// saved; scoring subscribes and rescores the week, which drops the game out of everybody's
/// points and out of <c>ActiveGameCount</c>.
/// </summary>
/// <remarks>
/// League-scoped for the same reason as <see cref="ResultOverridden"/>: a void is one league's
/// decision about its own row. The row itself is never deleted, so the game still renders as
/// "Voided" in the grid and in pick history (D-008's rule extended past lock).
/// </remarks>
/// <param name="LeagueId">The league whose set was changed.</param>
/// <param name="Week">The league's week number.</param>
/// <param name="WeekGameSetId">The <c>WeekGameSets.Id</c> to rescore.</param>
/// <param name="GameSetGameId">The voided <c>WeekGameSetGames.Id</c>.</param>
/// <param name="GameId">The underlying <c>Games.Id</c>, for logs and notifications.</param>
/// <param name="Reason">Why, as typed by the commissioner and stored on the audit row.</param>
/// <param name="ActorMembershipId">The commissioner who did it, matching the audit row.</param>
public sealed record GameVoided(
    Guid LeagueId,
    int Week,
    Guid WeekGameSetId,
    Guid GameSetGameId,
    Guid GameId,
    string Reason,
    Guid ActorMembershipId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTime OccurredUtc { get; init; }
}
