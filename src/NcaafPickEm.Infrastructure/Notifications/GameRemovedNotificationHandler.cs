using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.GameSets.Events;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Infrastructure.Push;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Notifications;

/// <summary>
/// Catalog #5 (Feature 11): tells a member a game they had picked was taken out of the week
/// before lock (postponed, cancelled, or removed by the commissioner). Reacts to
/// <see cref="GameRemovedFromSet"/>.
/// </summary>
/// <remarks>
/// Recipients are every <em>active</em> member with a <c>Picks</c> row on the removed
/// <c>WeekGameSetGames</c> row, regardless of their overall submission status — the card's rule
/// is "who had a pick on that game", not "who had submitted". A removed membership
/// (<c>RemovedUtc</c> set) keeps its pick rows for the locked-week history (04 section 6) but is
/// no longer in the league, so it is never a recipient, exactly as in
/// <see cref="ReminderRecipients"/>.
/// </remarks>
public sealed class GameRemovedNotificationHandler : IDomainEventHandler<GameRemovedFromSet>
{
    private readonly AppDbContext _database;
    private readonly NotificationService _notifications;
    private readonly ILogger<GameRemovedNotificationHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public GameRemovedNotificationHandler(
        AppDbContext database,
        NotificationService notifications,
        ILogger<GameRemovedNotificationHandler> logger)
    {
        _database = database;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(GameRemovedFromSet domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == domainEvent.WeekGameSetId, cancellationToken);

        if (set is null || set.LockedUtc is not null)
        {
            return;
        }

        Game? game = await _database.Games
            .AsNoTracking()
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .FirstOrDefaultAsync(g => g.Id == domainEvent.GameId, cancellationToken);

        if (game?.HomeTeam is null || game.AwayTeam is null)
        {
            _logger.LogWarning(
                "GameRemoved for game {GameId} could not resolve both teams; no notification sent",
                domainEvent.GameId);
            return;
        }

        List<Guid> recipients = await _database.Picks
            .AsNoTracking()
            .Where(p => p.WeekGameSetGameId == domainEvent.GameSetGameId)
            .Join(
                _database.Memberships.Where(m => m.RemovedUtc == null),
                p => p.MembershipId,
                m => m.Id,
                (p, m) => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            return;
        }

        PushPayload payload = NotificationMessages.GameRemoved(
            domainEvent.Week,
            game.AwayTeam.School,
            game.HomeTeam.School,
            domainEvent.LeagueId,
            domainEvent.GameSetGameId);

        int sent = 0;

        foreach (Guid userId in recipients)
        {
            NotificationResult result = await _notifications.SendToUserAsync(
                userId,
                NotificationType.GameRemoved,
                domainEvent.LeagueId,
                domainEvent.Week,
                payload,
                ttl: null,
                cancellationToken);

            if (result == NotificationResult.Sent)
            {
                sent++;
            }
        }

        _logger.LogInformation(
            "GameRemoved ({AwayTeam} vs {HomeTeam}) for league {LeagueId} week {Week}: notified {Recipients} member(s) with a pick, {Sent} sent",
            game.AwayTeam.School,
            game.HomeTeam.School,
            domainEvent.LeagueId,
            domainEvent.Week,
            recipients.Count,
            sent);
    }
}
