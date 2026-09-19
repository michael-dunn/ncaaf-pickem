using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Reacts to <see cref="GameAddedToSet"/> (Feature 04, P4-04): recomputes every existing submission
/// row for the week (a newly active game can revert a <c>Submitted</c> member to
/// <c>InProgress</c>, per <c>04-Domain-Algorithms.md</c> section 4 and <c>WeekGameSetGames.AddedUtc</c>,
/// D-085) and raises <c>HasUnseenGameChanges</c> for every one of them, so the picks page highlights
/// what changed. A member with no submission row yet (<c>NotStarted</c>, never touched picks) gets
/// no row created and no flag - they have nothing new to be told about.
/// </summary>
/// <remarks>
/// Order relative to P7-03's notification handler for the same event does not matter: that handler
/// computes its own recipients from <c>Status == Submitted OR SubmittedUtc != null</c> read at
/// dispatch time, not from anything this handler writes.
/// <para>
/// Idempotent: <see cref="PickService.RecomputeWeekStatusesAsync"/> derives status purely from the
/// current active games and picks, so replaying the same event twice (a crash between save and
/// dispatch, say) writes the same answer both times and never duplicates a row - the unique index
/// on <c>(WeekGameSetId, MembershipId)</c> is a find-or-create, not an insert.
/// </para>
/// </remarks>
public sealed class GameAddedPickHandler : IDomainEventHandler<GameAddedToSet>
{
    private readonly AppDbContext _database;
    private readonly PickService _pickService;

    /// <summary>Creates the handler.</summary>
    public GameAddedPickHandler(AppDbContext database, PickService pickService)
    {
        _database = database;
        _pickService = pickService;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameAddedToSet domainEvent, CancellationToken cancellationToken)
    {
        List<Guid> existingMembershipIds = await _database.WeekSubmissions
            .Where(submission => submission.WeekGameSetId == domainEvent.WeekGameSetId)
            .Select(submission => submission.MembershipId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingMembershipIds.Count == 0)
        {
            // Nobody has looked at picks for this week yet: nothing to revert, nothing to flag.
            return;
        }

        await _pickService.RecomputeWeekStatusesAsync(domainEvent.WeekGameSetId, existingMembershipIds, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Reacts to <see cref="GameRemovedFromSet"/> (Feature 04, P4-04): recomputes every existing
/// submission row for the week (a removed game changes <c>TotalCount</c>/<c>PickedCount</c> for
/// everyone, but a <c>Submitted</c> member stays <c>Submitted</c> per section 4) and raises
/// <c>HasUnseenGameChanges</c> only for members who had a pick on the removed row - that pick is
/// kept (D-008), so a member who never picked that game has nothing new to be told about.
/// </summary>
/// <remarks>
/// Order relative to P7-03's notification handler does not matter, for the same reason as
/// <see cref="GameAddedPickHandler"/>. Idempotent for the same reason too.
/// </remarks>
public sealed class GameRemovedPickHandler : IDomainEventHandler<GameRemovedFromSet>
{
    private readonly AppDbContext _database;
    private readonly PickService _pickService;

    /// <summary>Creates the handler.</summary>
    public GameRemovedPickHandler(AppDbContext database, PickService pickService)
    {
        _database = database;
        _pickService = pickService;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameRemovedFromSet domainEvent, CancellationToken cancellationToken)
    {
        List<Guid> membershipIdsWithPickOnRemovedGame = await _database.Picks
            .Where(pick => pick.WeekGameSetGameId == domainEvent.GameSetGameId)
            .Select(pick => pick.MembershipId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await _pickService.RecomputeWeekStatusesAsync(
            domainEvent.WeekGameSetId, membershipIdsWithPickOnRemovedGame, cancellationToken)
            .ConfigureAwait(false);
    }
}
