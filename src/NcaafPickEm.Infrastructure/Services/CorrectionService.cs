using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Events;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Scoring.Events;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Events;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Commissioner corrections (Feature 06, P5-02): overriding a locked week's game result, voiding
/// a game outright, and reading the league's audit trail back. Writes the <c>WeekGameSetGames</c>
/// column and the <c>AuditLog</c> row, saves, then raises <see cref="ResultOverridden"/> /
/// <see cref="GameVoided"/> - P5-01's already-subscribed handlers do the rescoring. This service
/// adds no scoring logic of its own (AGENT-NOTES.md, "Scoring").
/// </summary>
public sealed class CorrectionService
{
    /// <summary>Newest-first cap on <see cref="GetAuditAsync"/>.</summary>
    public const int AuditCap = 200;

    private static readonly string LockedMessage =
        "This week is not locked yet: overriding a result or voiding a game is a post-lock action.";

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly DomainEventCollector _collector;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly GameSetService _gameSetService;
    private readonly ILogger<CorrectionService> _logger;

    /// <summary>Creates the service.</summary>
    public CorrectionService(
        AppDbContext database,
        TimeProvider timeProvider,
        DomainEventCollector collector,
        IDomainEventDispatcher dispatcher,
        GameSetService gameSetService,
        ILogger<CorrectionService> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _collector = collector;
        _dispatcher = dispatcher;
        _gameSetService = gameSetService;
        _logger = logger;
    }

    /// <summary>
    /// Corrects one locked game's winner by hand. 409 before lock (<see cref="CorrectionRuleViolationCode.NotLocked"/>),
    /// 409 on an already-voided row (<see cref="CorrectionRuleViolationCode.AlreadyVoided"/>), 400
    /// when <paramref name="winnerTeamId"/> is not the game's home or away team
    /// (<see cref="CorrectionRuleViolationCode.TeamNotInGame"/>).
    /// </summary>
    /// <remarks>
    /// A second, identical override is not a no-op: it writes a fresh audit row and re-raises
    /// <see cref="ResultOverridden"/>, exactly as a genuine change would. The rescore it triggers
    /// recomputes the same numbers, so nothing about the leaderboard moves - but the audit trail
    /// gets an honest second entry ("commissioner confirmed the call again") instead of the
    /// service silently guessing that a repeated call must be a mistake worth swallowing.
    /// </remarks>
    public async Task<GameSetGameDto> OverrideResultAsync(
        Membership caller,
        int week,
        Guid gameId,
        Guid winnerTeamId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Guid leagueId = caller.LeagueId;
        WeekGameSetGame row = await LoadLockedRowAsync(leagueId, week, gameId, cancellationToken)
            .ConfigureAwait(false);

        if (row.IsVoided)
        {
            throw new CorrectionRuleViolation(
                CorrectionRuleViolationCode.AlreadyVoided,
                "This game has been voided; it cannot also be overridden.");
        }

        Game game = row.Game!;
        if (winnerTeamId != game.HomeTeamId && winnerTeamId != game.AwayTeamId)
        {
            throw new CorrectionRuleViolation(
                CorrectionRuleViolationCode.TeamNotInGame,
                "The winner must be this game's home or away team.");
        }

        Guid? before = row.ResultOverrideWinnerTeamId;
        row.ResultOverrideWinnerTeamId = winnerTeamId;

        WriteAudit(
            caller,
            AuditAction.ResultOverride,
            row.Id,
            new
            {
                gameId,
                before,
                after = winnerTeamId,
                reason,
                homeScore = game.HomeScore,
                awayScore = game.AwayScore,
            });

        _collector.Raise(new ResultOverridden(leagueId, week, row.WeekGameSetId, row.Id, gameId, winnerTeamId, caller.Id)
        {
            OccurredUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });

        await SaveAndDispatchAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "League {LeagueId} week {Week}: membership {ActorMembershipId} overrode game {GameId} to winner {WinnerTeamId}.",
            leagueId,
            week,
            caller.Id,
            gameId,
            winnerTeamId);

        return await GetGameDtoAsync(leagueId, week, row.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Voids one locked game so it scores nothing for anybody and drops out of
    /// <c>ActiveGameCount</c>. 409 before lock, 409 on a second void of the same row.
    /// </summary>
    public async Task<GameSetGameDto> VoidGameAsync(
        Membership caller,
        int week,
        Guid gameId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Guid leagueId = caller.LeagueId;
        WeekGameSetGame row = await LoadLockedRowAsync(leagueId, week, gameId, cancellationToken)
            .ConfigureAwait(false);

        if (row.IsVoided)
        {
            throw new CorrectionRuleViolation(
                CorrectionRuleViolationCode.AlreadyVoided,
                "This game has already been voided.");
        }

        Game game = row.Game!;
        row.IsVoided = true;

        WriteAudit(
            caller,
            AuditAction.GameVoided,
            row.Id,
            new { gameId, reason, homeScore = game.HomeScore, awayScore = game.AwayScore });

        _collector.Raise(new GameVoided(leagueId, week, row.WeekGameSetId, row.Id, gameId, reason, caller.Id)
        {
            OccurredUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });

        await SaveAndDispatchAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "League {LeagueId} week {Week}: membership {ActorMembershipId} voided game {GameId} ({Reason}).",
            leagueId,
            week,
            caller.Id,
            gameId,
            reason);

        return await GetGameDtoAsync(leagueId, week, row.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The league's audit trail, newest first, capped at <see cref="AuditCap"/> — visible to
    /// every member (Feature 06).
    /// </summary>
    public async Task<AuditEntry[]> GetAuditAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        List<AuditLogEntry> rows = await _database.AuditLog
            .AsNoTracking()
            .Include(entry => entry.ActorMembership!.User)
            .Where(entry => entry.LeagueId == leagueId)
            .OrderByDescending(entry => entry.CreatedUtc)
            .Take(AuditCap)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, AuditGameNames> games = await LoadGameNamesAsync(rows, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, string> memberNames = await LoadMemberNamesAsync(rows, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new AuditEntry(
            new DateTimeOffset(row.CreatedUtc, TimeSpan.Zero),
            row.ActorMembership is { } actor ? MemberNameProjection.Effective(actor) : "System",
            row.Action.ToString(),
            AuditSummaryBuilder.Build(row, games, memberNames)))];
    }

    /// <summary>
    /// Loads the active, locked row a correction acts on. 404 when the game is not part of the
    /// week's set (or has been manually removed); 409 before lock.
    /// </summary>
    private async Task<WeekGameSetGame> LoadLockedRowAsync(
        Guid leagueId,
        int week,
        Guid gameId,
        CancellationToken cancellationToken)
    {
        await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await _database.WeekGameSets
            .FirstOrDefaultAsync(candidate => candidate.LeagueId == leagueId && candidate.Week == week, cancellationToken)
            .ConfigureAwait(false);

        if (set is null || !WeekGameSetLockGuard.IsFrozen(set, _timeProvider.GetUtcNow().UtcDateTime))
        {
            throw new CorrectionRuleViolation(CorrectionRuleViolationCode.NotLocked, LockedMessage);
        }

        WeekGameSetGame? row = await _database.WeekGameSetGames
            .Include(candidate => candidate.Game)
            .FirstOrDefaultAsync(
                candidate => candidate.WeekGameSetId == set.Id && candidate.GameId == gameId && !candidate.IsRemoved,
                cancellationToken)
            .ConfigureAwait(false);

        return row ?? throw new CorrectionRuleViolation(
            CorrectionRuleViolationCode.GameNotFound,
            "No such active game in this week.");
    }

    private async Task SaveAndDispatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<IDomainEvent> raised = _collector.TakeAll();
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.DispatchAsync(raised, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GameSetGameDto> GetGameDtoAsync(
        Guid leagueId,
        int week,
        Guid gameSetGameId,
        CancellationToken cancellationToken)
    {
        WeekGameSetResponse response = await _gameSetService
            .GetWeekGameSetAsync(leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        return response.Games.First(game => game.GameSetGameId == gameSetGameId);
    }

    private async Task<Dictionary<Guid, AuditGameNames>> LoadGameNamesAsync(
        List<AuditLogEntry> rows,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> gameIds = [];
        foreach (AuditLogEntry row in rows)
        {
            if (IsGameAction(row.Action) && TryGetGuid(row.Details, "gameId", out Guid gameId))
            {
                gameIds.Add(gameId);
            }
        }

        if (gameIds.Count == 0)
        {
            return [];
        }

        List<AuditGameNames> games = await _database.Games
            .AsNoTracking()
            .Where(game => gameIds.Contains(game.Id))
            .Select(game => new AuditGameNames(
                game.Id,
                game.HomeTeamId,
                game.AwayTeamId,
                game.HomeTeam!.School,
                game.AwayTeam!.School))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return games.ToDictionary(game => game.GameId);
    }

    private async Task<Dictionary<Guid, string>> LoadMemberNamesAsync(
        List<AuditLogEntry> rows,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> membershipIds = [];
        foreach (AuditLogEntry row in rows)
        {
            if (IsMemberAction(row.Action) && row.TargetId is Guid targetId)
            {
                membershipIds.Add(targetId);
            }
        }

        if (membershipIds.Count == 0)
        {
            return [];
        }

        return await _database.Memberships
            .AsNoTracking()
            .Where(membership => membershipIds.Contains(membership.Id))
            .Select(membership => new { membership.Id, Name = membership.DisplayNameOverride ?? membership.User!.DisplayName })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsGameAction(AuditAction action) => action
        is AuditAction.ResultOverride
        or AuditAction.GameVoided
        or AuditAction.GameManuallyAdded
        or AuditAction.GameManuallyRemoved;

    private static bool IsMemberAction(AuditAction action) => action
        is AuditAction.MemberRemoved
        or AuditAction.RolePromoted
        or AuditAction.RoleDemoted
        or AuditAction.RoleTransferred;

    private static bool TryGetGuid(string detailsJson, string propertyName, out Guid value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(detailsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out JsonElement element)
                && element.ValueKind == JsonValueKind.String
                && Guid.TryParse(element.GetString(), out Guid parsed))
            {
                value = parsed;
                return true;
            }
        }
        catch (JsonException)
        {
            // A malformed or unexpected Details blob just yields no game context for the summary.
        }

        value = Guid.Empty;
        return false;
    }

    private void WriteAudit(Membership actor, AuditAction action, Guid? targetId, object details)
    {
        _database.AuditLog.Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            LeagueId = actor.LeagueId,
            ActorMembershipId = actor.Id,
            Action = action,
            TargetId = targetId,
            Details = JsonSerializer.Serialize(details),
            CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    /// <summary>One game's names, for summary building.</summary>
    private sealed record AuditGameNames(Guid GameId, Guid HomeTeamId, Guid AwayTeamId, string HomeTeam, string AwayTeam);

    /// <summary>
    /// Builds the one-line <see cref="AuditEntry.Summary"/> per <see cref="AuditAction"/>
    /// (AGENT-NOTES.md, "Corrections"). Every action this codebase writes today is covered;
    /// an action nobody recognizes falls back to its own name rather than throwing.
    /// </summary>
    private static class AuditSummaryBuilder
    {
        public static string Build(
            AuditLogEntry entry,
            Dictionary<Guid, AuditGameNames> games,
            Dictionary<Guid, string> memberNames)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(entry.Details);
                JsonElement details = document.RootElement;

                if (details.ValueKind != JsonValueKind.Object)
                {
                    // TryGetProperty throws InvalidOperationException on anything but an object,
                    // and that is not a JsonException (P8-01, D-193).
                    return entry.Action.ToString();
                }

                return entry.Action switch
                {
                    AuditAction.ResultOverride => BuildResultOverride(details, games),
                    AuditAction.GameVoided => BuildGameVoided(details, games),
                    AuditAction.GameManuallyAdded => BuildGameAdded(details, games),
                    AuditAction.GameManuallyRemoved => BuildGameRemoved(details, games),
                    AuditAction.MemberRemoved => $"Removed {MemberName(entry, memberNames)}",
                    AuditAction.RolePromoted => $"Promoted {MemberName(entry, memberNames)}",
                    AuditAction.RoleDemoted => $"Demoted {MemberName(entry, memberNames)}",
                    AuditAction.RoleTransferred => $"Transferred commissioner to {MemberName(entry, memberNames)}",
                    AuditAction.ManualRefresh => BuildManualRefresh(details),
                    _ => entry.Action.ToString(),
                };
            }
            catch (JsonException)
            {
                return entry.Action.ToString();
            }
        }

        private static string BuildResultOverride(JsonElement details, Dictionary<Guid, AuditGameNames> games)
        {
            if (!TryGame(details, games, out AuditGameNames game)
                || !TryGuid(details, "after", out Guid after))
            {
                return "Overrode a game result";
            }

            string winner = after == game.HomeTeamId ? game.HomeTeam : game.AwayTeam;
            string reason = ReasonOf(details);
            return $"Set {winner} as winner of {game.AwayTeam} @ {game.HomeTeam}: {reason}";
        }

        private static string BuildGameVoided(JsonElement details, Dictionary<Guid, AuditGameNames> games)
        {
            if (!TryGame(details, games, out AuditGameNames game))
            {
                return "Voided a game";
            }

            return $"Voided {game.AwayTeam} @ {game.HomeTeam}: {ReasonOf(details)}";
        }

        private static string BuildGameAdded(JsonElement details, Dictionary<Guid, AuditGameNames> games)
        {
            string week = WeekOf(details);
            if (!TryGame(details, games, out AuditGameNames game))
            {
                return $"Added a game to week {week}";
            }

            return $"Added {game.AwayTeam} @ {game.HomeTeam} to week {week}";
        }

        private static string BuildGameRemoved(JsonElement details, Dictionary<Guid, AuditGameNames> games)
        {
            string week = WeekOf(details);
            if (!TryGame(details, games, out AuditGameNames game))
            {
                return $"Removed a game from week {week}";
            }

            return $"Removed {game.AwayTeam} @ {game.HomeTeam} from week {week}";
        }

        private static string BuildManualRefresh(JsonElement details) =>
            details.TryGetProperty("DataType", out JsonElement dataType) && dataType.ValueKind == JsonValueKind.String
                ? $"Refreshed {dataType.GetString()}"
                : "Refreshed data";

        private static string MemberName(AuditLogEntry entry, Dictionary<Guid, string> memberNames) =>
            entry.TargetId is Guid targetId && memberNames.TryGetValue(targetId, out string? name)
                ? name
                : "a member";

        private static bool TryGame(JsonElement details, Dictionary<Guid, AuditGameNames> games, out AuditGameNames game)
        {
            game = default!;
            if (!details.TryGetProperty("gameId", out JsonElement gameIdElement)
                || gameIdElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(gameIdElement.GetString(), out Guid gameId)
                || !games.TryGetValue(gameId, out AuditGameNames? found))
            {
                return false;
            }

            game = found;
            return true;
        }

        /// <summary>
        /// Every reader here is total: a <c>Details</c> blob that is valid JSON but the wrong
        /// shape must degrade to a vaguer sentence, never throw. <c>JsonElement.GetProperty</c>
        /// and the typed getters raise <see cref="KeyNotFoundException"/> and
        /// <see cref="InvalidOperationException"/>, neither of which the <c>JsonException</c>
        /// catch above would stop, so one bad row would 500 the whole audit list (P8-01, D-193).
        /// </summary>
        private static string WeekOf(JsonElement details) =>
            details.TryGetProperty("week", out JsonElement week) && week.ValueKind == JsonValueKind.Number
                ? week.GetInt32().ToString(CultureInfo.InvariantCulture)
                : "?";

        private static bool TryGuid(JsonElement details, string propertyName, out Guid value)
        {
            value = Guid.Empty;

            return details.TryGetProperty(propertyName, out JsonElement element)
                && element.ValueKind == JsonValueKind.String
                && Guid.TryParse(element.GetString(), out value);
        }

        private static string ReasonOf(JsonElement details) =>
            details.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString() ?? string.Empty
                : string.Empty;
    }
}
