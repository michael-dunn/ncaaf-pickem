using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Cfbd;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Ingests reference data from any <see cref="IReferenceDataProvider"/> — <c>Cfbd</c> in
/// production, the fixture provider in tests — into the database, keeping
/// <c>DataRefreshStatus</c> current on every attempt (Feature 09, 12).
/// </summary>
/// <remarks>
/// Every method follows the same shape: record the attempt, do the work entirely in the tracked
/// change graph without calling <c>SaveChangesAsync</c> until the very end, then
/// either commit success once or clear the change tracker and record just the failure. That
/// ordering is what makes "failures keep prior data" true without a database transaction: nothing
/// reaches SQL until the whole ingest has succeeded in memory.
/// </remarks>
public sealed class ReferenceDataIngestService
{
    /// <summary>
    /// <see cref="RefreshDataType"/> has no entry for the season calendar; <see cref="IngestCalendarAsync"/>
    /// records under <see cref="RefreshDataType.Schedule"/> because <c>SeasonWeeks</c> and
    /// <c>Games</c> are both schedule-domain data and the data status page has one row per slice,
    /// not per table (see DECISIONS.md).
    /// </summary>
    private const RefreshDataType CalendarRefreshDataType = RefreshDataType.Schedule;

    private readonly AppDbContext _database;
    private readonly IReferenceDataProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReferenceDataIngestService> _logger;

    /// <summary>Creates the service.</summary>
    public ReferenceDataIngestService(
        AppDbContext database,
        IReferenceDataProvider provider,
        TimeProvider timeProvider,
        ILogger<ReferenceDataIngestService> logger)
    {
        _database = database;
        _provider = provider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Upserts <c>Conferences</c> and <c>Teams</c> keyed by CFBD id, and seeds
    /// <c>TeamAliases(Source=Cfbd)</c> from every team's <c>alternateNames</c>.
    /// </summary>
    public Task<TeamsIngestResult> IngestTeamsAsync(int season, CancellationToken cancellationToken = default) =>
        RunAsync(
            RefreshDataType.Teams,
            async () =>
            {
                IReadOnlyList<ProviderConference> providerConferences =
                    await _provider.GetConferencesAsync(season, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<ProviderTeam> providerTeams =
                    await _provider.GetTeamsAsync(season, cancellationToken).ConfigureAwait(false);

                Dictionary<int, Guid> conferenceIds =
                    await UpsertConferencesAsync(providerConferences, cancellationToken).ConfigureAwait(false);
                Dictionary<int, Guid> teamIds =
                    await UpsertTeamsAsync(providerTeams, conferenceIds, cancellationToken).ConfigureAwait(false);
                int aliasCount = await UpsertAliasesAsync(providerTeams, teamIds, cancellationToken).ConfigureAwait(false);

                return new TeamsIngestResult(true, null, conferenceIds.Count, teamIds.Count, aliasCount);
            },
            error => new TeamsIngestResult(false, error, 0, 0, 0),
            cancellationToken);

    /// <summary>
    /// Upserts <c>SeasonWeeks</c> from CFBD's calendar, normalized per
    /// <see cref="CfbdCalendarNormalization"/>.
    /// </summary>
    public Task<CalendarIngestResult> IngestCalendarAsync(int season, CancellationToken cancellationToken = default) =>
        RunAsync(
            CalendarRefreshDataType,
            async () =>
            {
                IReadOnlyList<ProviderCalendarWeek> providerWeeks =
                    await _provider.GetCalendarAsync(season, cancellationToken).ConfigureAwait(false);

                Dictionary<int, SeasonWeek> existingByWeek = await _database.SeasonWeeks
                    .Where(week => week.SeasonYear == season)
                    .ToDictionaryAsync(week => week.Week, cancellationToken)
                    .ConfigureAwait(false);

                int count = 0;
                foreach (ProviderCalendarWeek providerWeek in providerWeeks)
                {
                    SeasonWeek normalized = CfbdCalendarNormalization.Normalize(providerWeek);

                    if (existingByWeek.TryGetValue(providerWeek.Week, out SeasonWeek? existingWeek))
                    {
                        if (existingWeek != normalized)
                        {
                            _database.Entry(existingWeek).CurrentValues.SetValues(normalized);
                        }
                    }
                    else
                    {
                        _database.SeasonWeeks.Add(normalized);
                    }

                    count++;
                }

                return new CalendarIngestResult(true, null, count);
            },
            error => new CalendarIngestResult(false, error, 0),
            cancellationToken);

    /// <summary>
    /// Upserts <c>Games</c> for one (season, week) by <c>CfbdGameId</c>. Never overwrites the
    /// live-score fields (<c>Status</c>/<c>HomeScore</c>/<c>AwayScore</c>/<c>Period</c>/<c>Clock</c>)
    /// P2-03 owns, except that a <c>Completed=true</c> game with both point totals is promoted to
    /// <see cref="GameStatus.Final"/> when it is not already Final (CFBD has no status field to
    /// read otherwise). Postponement is detected as absence: a previously known
    /// <see cref="GameStatus.Scheduled"/>/<see cref="GameStatus.InProgress"/> game missing from
    /// this fetch becomes <see cref="GameStatus.Postponed"/>; a <see cref="GameStatus.Postponed"/>
    /// game that reappears goes back to <see cref="GameStatus.Scheduled"/>. See DECISIONS.md.
    /// </summary>
    public Task<ScheduleIngestResult> IngestScheduleAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            RefreshDataType.Schedule,
            async () =>
            {
                IReadOnlyList<ProviderGame> providerGames =
                    await _provider.GetGamesAsync(season, week, cancellationToken).ConfigureAwait(false);

                List<Game> existingGames = await _database.Games
                    .Where(game => game.SeasonYear == season && game.Week == week)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Guard: a payload that lost more than half of a week we already had is treated
                // as a bad refresh, not a suddenly-shorter schedule. Nothing is written.
                if (existingGames.Count > 0 && providerGames.Count < existingGames.Count / 2.0)
                {
                    throw new InvalidOperationException(
                        $"CFBD returned {providerGames.Count} games for season {season} week {week}, " +
                        $"fewer than half of the {existingGames.Count} already known; refusing the update.");
                }

                Dictionary<int, Guid> teamIdsByCfbdId = await _database.Teams
                    .ToDictionaryAsync(team => team.CfbdId, team => team.Id, cancellationToken)
                    .ConfigureAwait(false);

                Dictionary<long, Game> existingByCfbdId = existingGames.ToDictionary(game => game.CfbdGameId);
                var seenCfbdGameIds = new HashSet<long>();

                int upserted = 0;
                int restored = 0;

                foreach (ProviderGame providerGame in providerGames)
                {
                    if (!teamIdsByCfbdId.TryGetValue(providerGame.HomeCfbdTeamId, out Guid homeTeamId)
                        || !teamIdsByCfbdId.TryGetValue(providerGame.AwayCfbdTeamId, out Guid awayTeamId))
                    {
                        _logger.LogWarning(
                            "Skipping CFBD game {CfbdGameId}: home or away team not yet ingested",
                            providerGame.CfbdGameId);
                        continue;
                    }

                    if (providerGame.StartTimeTbd)
                    {
                        _logger.LogInformation(
                            "CFBD game {CfbdGameId} has a TBD kickoff; keeping the placeholder KickoffUtc",
                            providerGame.CfbdGameId);
                    }

                    seenCfbdGameIds.Add(providerGame.CfbdGameId);

                    DateTimeOffset kickoffOffset = new(DateTime.SpecifyKind(providerGame.KickoffUtc, DateTimeKind.Utc));
                    DateOnly kickoffEasternDate = DateOnly.FromDateTime(SeasonCalendar.ToEastern(kickoffOffset).DateTime);
                    bool isSaturdayEastern = SeasonCalendar.IsSaturdayEastern(kickoffOffset);
                    bool canPromoteToFinal = providerGame.Completed
                        && providerGame.HomePoints is int homePoints
                        && providerGame.AwayPoints is int awayPoints;

                    if (existingByCfbdId.TryGetValue(providerGame.CfbdGameId, out Game? existing))
                    {
                        existing.SeasonYear = providerGame.Season;
                        existing.Week = providerGame.Week;
                        existing.HomeTeamId = homeTeamId;
                        existing.AwayTeamId = awayTeamId;
                        existing.KickoffUtc = providerGame.KickoffUtc;
                        existing.KickoffEasternDate = kickoffEasternDate;
                        existing.IsSaturdayEastern = isSaturdayEastern;
                        existing.IsConferenceGame = providerGame.IsConferenceGame;
                        existing.Venue = providerGame.Venue;

                        if (existing.Status == GameStatus.Postponed)
                        {
                            existing.Status = GameStatus.Scheduled;
                            restored++;
                        }

                        if (canPromoteToFinal && existing.Status != GameStatus.Final)
                        {
                            existing.Status = GameStatus.Final;
                            existing.HomeScore = providerGame.HomePoints;
                            existing.AwayScore = providerGame.AwayPoints;
                        }
                    }
                    else
                    {
                        GameStatus initialStatus = canPromoteToFinal ? GameStatus.Final : GameStatus.Scheduled;

                        _database.Games.Add(new Game
                        {
                            Id = Guid.CreateVersion7(),
                            CfbdGameId = providerGame.CfbdGameId,
                            SeasonYear = providerGame.Season,
                            Week = providerGame.Week,
                            HomeTeamId = homeTeamId,
                            AwayTeamId = awayTeamId,
                            KickoffUtc = providerGame.KickoffUtc,
                            KickoffEasternDate = kickoffEasternDate,
                            IsSaturdayEastern = isSaturdayEastern,
                            IsConferenceGame = providerGame.IsConferenceGame,
                            Status = initialStatus,
                            HomeScore = initialStatus == GameStatus.Final ? providerGame.HomePoints : null,
                            AwayScore = initialStatus == GameStatus.Final ? providerGame.AwayPoints : null,
                            Venue = providerGame.Venue,
                        });
                    }

                    upserted++;
                }

                int postponed = 0;
                foreach (Game existing in existingGames)
                {
                    if (seenCfbdGameIds.Contains(existing.CfbdGameId))
                    {
                        continue;
                    }

                    if (existing.Status is GameStatus.Scheduled or GameStatus.InProgress)
                    {
                        existing.Status = GameStatus.Postponed;
                        postponed++;
                    }
                }

                return new ScheduleIngestResult(true, null, upserted, postponed, restored);
            },
            error => new ScheduleIngestResult(false, error, 0, 0, 0),
            cancellationToken);

    /// <summary>
    /// Replaces the AP poll rows for one (season, week) with a fresh <c>FetchedUtc</c>. Ranks
    /// missing from the new payload are removed; ranks present are updated in place so the
    /// primary key (SeasonYear, Week, Poll, Rank) is never deleted and re-inserted in the same
    /// batch.
    /// </summary>
    public Task<RankingsIngestResult> IngestRankingsAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            RefreshDataType.Rankings,
            async () =>
            {
                IReadOnlyList<ProviderRanking> providerRankings =
                    await _provider.GetRankingsAsync(season, week, cancellationToken).ConfigureAwait(false);

                Dictionary<int, Guid> teamIdsByCfbdId = await _database.Teams
                    .ToDictionaryAsync(team => team.CfbdId, team => team.Id, cancellationToken)
                    .ConfigureAwait(false);

                List<Ranking> existing = await _database.Rankings
                    .Where(r => r.SeasonYear == season && r.Week == week && r.Poll == Ranking.ApPoll)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                Dictionary<int, Ranking> existingByRank = existing.ToDictionary(r => r.Rank);

                DateTime fetchedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var keepRanks = new HashSet<int>();
                int count = 0;

                foreach (ProviderRanking providerRanking in providerRankings)
                {
                    if (!teamIdsByCfbdId.TryGetValue(providerRanking.CfbdTeamId, out Guid teamId))
                    {
                        continue;
                    }

                    keepRanks.Add(providerRanking.Rank);

                    if (existingByRank.TryGetValue(providerRanking.Rank, out Ranking? existingRank))
                    {
                        existingRank.TeamId = teamId;
                        existingRank.FetchedUtc = fetchedUtc;
                    }
                    else
                    {
                        _database.Rankings.Add(new Ranking
                        {
                            SeasonYear = season,
                            Week = week,
                            Poll = Ranking.ApPoll,
                            Rank = providerRanking.Rank,
                            TeamId = teamId,
                            FetchedUtc = fetchedUtc,
                        });
                    }

                    count++;
                }

                foreach (Ranking stale in existing.Where(r => !keepRanks.Contains(r.Rank)))
                {
                    _database.Rankings.Remove(stale);
                }

                return new RankingsIngestResult(true, null, count);
            },
            error => new RankingsIngestResult(false, error, 0),
            cancellationToken);

    /// <summary>
    /// Appends one <c>GameLines</c> row per provider line whose spread differs from the current
    /// (newest) row for that (game, provider) — a repeated ingest with an unchanged spread grows
    /// nothing. Identical Provider+Spread pairs within the same fetch are added once.
    /// </summary>
    public Task<LinesIngestResult> IngestLinesAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            RefreshDataType.Lines,
            async () =>
            {
                IReadOnlyList<ProviderLine> providerLines =
                    await _provider.GetLinesAsync(season, week, cancellationToken).ConfigureAwait(false);

                Dictionary<long, Guid> gameIdsByCfbdId = await _database.Games
                    .Where(game => game.SeasonYear == season && game.Week == week)
                    .ToDictionaryAsync(game => game.CfbdGameId, game => game.Id, cancellationToken)
                    .ConfigureAwait(false);

                List<Guid> gameIds = [.. gameIdsByCfbdId.Values];
                List<GameLine> existingLines = gameIds.Count == 0
                    ? []
                    : await _database.GameLines
                        .Where(line => gameIds.Contains(line.GameId))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                Dictionary<(Guid GameId, string Provider), GameLine> currentByKey = existingLines
                    .GroupBy(line => (line.GameId, line.Provider))
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.FetchedUtc).First());

                DateTime fetchedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var seenThisFetch = new HashSet<(Guid GameId, string Provider, decimal Spread)>();
                int added = 0;

                foreach (ProviderLine providerLine in providerLines)
                {
                    if (providerLine.Spread is not decimal spread
                        || !gameIdsByCfbdId.TryGetValue(providerLine.CfbdGameId, out Guid gameId))
                    {
                        continue;
                    }

                    string providerName = Truncate(providerLine.Provider, GameLine.ProviderMaxLength);

                    if (!seenThisFetch.Add((gameId, providerName, spread)))
                    {
                        continue;
                    }

                    (Guid GameId, string Provider) gameProviderKey = (gameId, providerName);
                    if (currentByKey.TryGetValue(gameProviderKey, out GameLine? current) && current.Spread == spread)
                    {
                        continue;
                    }

                    var line = new GameLine
                    {
                        Id = Guid.CreateVersion7(),
                        GameId = gameId,
                        Provider = providerName,
                        Spread = spread,
                        FetchedUtc = fetchedUtc,
                    };
                    _database.GameLines.Add(line);
                    currentByKey[gameProviderKey] = line;
                    added++;
                }

                return new LinesIngestResult(true, null, added);
            },
            error => new LinesIngestResult(false, error, 0),
            cancellationToken);

    private async Task<Dictionary<int, Guid>> UpsertConferencesAsync(
        IReadOnlyList<ProviderConference> conferences,
        CancellationToken cancellationToken)
    {
        Dictionary<int, Conference> existingByCfbdId = await _database.Conferences
            .ToDictionaryAsync(c => c.CfbdId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, Guid> ids = [];
        foreach (ProviderConference providerConference in conferences)
        {
            if (existingByCfbdId.TryGetValue(providerConference.CfbdId, out Conference? existing))
            {
                existing.Name = providerConference.Name;
                existing.Abbreviation = providerConference.Abbreviation;
                existing.Classification = providerConference.Classification;
                ids[providerConference.CfbdId] = existing.Id;
            }
            else
            {
                var conference = new Conference
                {
                    Id = Guid.CreateVersion7(),
                    CfbdId = providerConference.CfbdId,
                    Name = providerConference.Name,
                    Abbreviation = providerConference.Abbreviation,
                    Classification = providerConference.Classification,
                };
                _database.Conferences.Add(conference);
                ids[providerConference.CfbdId] = conference.Id;
            }
        }

        return ids;
    }

    private async Task<Dictionary<int, Guid>> UpsertTeamsAsync(
        IReadOnlyList<ProviderTeam> teams,
        Dictionary<int, Guid> conferenceIdsByCfbdId,
        CancellationToken cancellationToken)
    {
        Dictionary<int, Team> existingByCfbdId = await _database.Teams
            .ToDictionaryAsync(team => team.CfbdId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, Guid> ids = [];
        foreach (ProviderTeam providerTeam in teams)
        {
            Guid? conferenceId = providerTeam.ConferenceCfbdId is int cfbdConferenceId
                && conferenceIdsByCfbdId.TryGetValue(cfbdConferenceId, out Guid mappedId)
                    ? mappedId
                    : null;

            if (existingByCfbdId.TryGetValue(providerTeam.CfbdId, out Team? existing))
            {
                existing.School = providerTeam.School;
                existing.Mascot = providerTeam.Mascot;
                existing.Abbreviation = providerTeam.Abbreviation;
                existing.ConferenceId = conferenceId;
                existing.Classification = providerTeam.Classification;
                existing.LogoUrl = providerTeam.LogoUrl;
                ids[providerTeam.CfbdId] = existing.Id;
            }
            else
            {
                var team = new Team
                {
                    Id = Guid.CreateVersion7(),
                    CfbdId = providerTeam.CfbdId,
                    School = providerTeam.School,
                    Mascot = providerTeam.Mascot,
                    Abbreviation = providerTeam.Abbreviation,
                    ConferenceId = conferenceId,
                    Classification = providerTeam.Classification,
                    LogoUrl = providerTeam.LogoUrl,
                };
                _database.Teams.Add(team);
                ids[providerTeam.CfbdId] = team.Id;
            }
        }

        return ids;
    }

    private async Task<int> UpsertAliasesAsync(
        IReadOnlyList<ProviderTeam> teams,
        Dictionary<int, Guid> teamIdsByCfbdId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, TeamAlias> existingByAlias = await _database.TeamAliases
            .Where(alias => alias.Source == ProviderSource.Cfbd)
            .ToDictionaryAsync(alias => alias.Alias, cancellationToken)
            .ConfigureAwait(false);

        int count = 0;
        foreach (ProviderTeam providerTeam in teams)
        {
            if (!teamIdsByCfbdId.TryGetValue(providerTeam.CfbdId, out Guid teamId))
            {
                continue;
            }

            foreach (string alias in providerTeam.AlternateNames)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                string trimmedAlias = Truncate(alias, TeamAlias.AliasMaxLength);

                if (existingByAlias.TryGetValue(trimmedAlias, out TeamAlias? existing))
                {
                    existing.TeamId = teamId;
                }
                else
                {
                    var row = new TeamAlias
                    {
                        Id = Guid.CreateVersion7(),
                        TeamId = teamId,
                        Source = ProviderSource.Cfbd,
                        Alias = trimmedAlias,
                    };
                    _database.TeamAliases.Add(row);
                    existingByAlias[trimmedAlias] = row;
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Records the attempt, runs <paramref name="body"/> entirely against the tracked change
    /// graph, and commits success once; on failure the change tracker is cleared (discarding
    /// whatever <paramref name="body"/> added or modified, since nothing was ever saved) and only
    /// the failure is recorded.
    /// </summary>
    private async Task<TResult> RunAsync<TResult>(
        RefreshDataType dataType,
        Func<Task<TResult>> body,
        Func<string?, TResult> onFailure,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        DataRefreshStatus status = await LoadOrCreateStatusAsync(dataType, cancellationToken).ConfigureAwait(false);
        status.LastAttemptUtc = nowUtc;
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            TResult result = await body().ConfigureAwait(false);

            status.LastSuccessUtc = nowUtc;
            status.LastError = null;
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            // Nothing body() added or modified was ever saved (RunAsync's contract, see the class
            // remarks), so clearing the tracker discards it all; only the failure gets committed.
            _database.ChangeTracker.Clear();

            DataRefreshStatus failedStatus =
                await LoadOrCreateStatusAsync(dataType, cancellationToken).ConfigureAwait(false);
            failedStatus.LastAttemptUtc = nowUtc;
            failedStatus.LastError = Truncate(ex.Message, DataRefreshStatus.LastErrorMaxLength);
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogError(ex, "{DataType} ingest failed", dataType);

            return onFailure(failedStatus.LastError);
        }
    }

    private async Task<DataRefreshStatus> LoadOrCreateStatusAsync(RefreshDataType dataType, CancellationToken cancellationToken)
    {
        DataRefreshStatus? status = await _database.DataRefreshStatuses
            .FindAsync([dataType], cancellationToken)
            .ConfigureAwait(false);

        if (status is not null)
        {
            return status;
        }

        status = new DataRefreshStatus { DataType = dataType };
        _database.DataRefreshStatuses.Add(status);
        return status;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
