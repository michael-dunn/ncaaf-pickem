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

    /// <summary>How many colliding aliases the one summary warning names (D-168).</summary>
    private const int CollisionExamples = 5;

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
    public async Task<CalendarIngestResult> IngestCalendarAsync(int season, CancellationToken cancellationToken = default)
    {
        CalendarIngestResult result = await IngestCalendarCoreAsync(season, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            // DbSeasonWeekSource caches weeks per season for a few minutes; the rows just changed.
            DbSeasonWeekSource.Invalidate(season);
        }

        return result;
    }

    private Task<CalendarIngestResult> IngestCalendarCoreAsync(int season, CancellationToken cancellationToken) =>
        RunAsync(
            CalendarRefreshDataType,
            async () =>
            {
                IReadOnlyList<ProviderCalendarWeek> providerWeeks =
                    await _provider.GetCalendarAsync(season, cancellationToken).ConfigureAwait(false);

                CalendarNormalizationResult normalizedSeason =
                    CfbdCalendarNormalization.NormalizeSeason(providerWeeks);

                if (normalizedSeason.SkippedNonRegular > 0)
                {
                    _logger.LogInformation(
                        "Skipped {Skipped} non-regular-season calendar row(s) for {Season}; "
                        + "bowls and the playoff are out of scope (D-167)",
                        normalizedSeason.SkippedNonRegular,
                        season);
                }

                if (normalizedSeason.DuplicateWeeks.Count > 0)
                {
                    _logger.LogWarning(
                        "CFBD's calendar for {Season} repeated week number(s) {Weeks}; kept the first row of each",
                        season,
                        string.Join(", ", normalizedSeason.DuplicateWeeks));
                }

                Dictionary<int, SeasonWeek> existingByWeek = await _database.SeasonWeeks
                    .Where(week => week.SeasonYear == season)
                    .ToDictionaryAsync(week => week.Week, cancellationToken)
                    .ConfigureAwait(false);

                int count = 0;
                foreach (SeasonWeek normalized in normalizedSeason.Weeks)
                {
                    if (existingByWeek.TryGetValue(normalized.Week, out SeasonWeek? existingWeek))
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

                return new CalendarIngestResult(true, null, count, normalizedSeason.SkippedNonRegular);
            },
            error => new CalendarIngestResult(false, error, 0, 0),
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

                // Guard: an empty payload is never a real week. A provider that answers with no
                // games (rather than throwing) must not be mistaken for "this week has no
                // schedule" and must not stamp LastSuccessUtc.
                if (providerGames.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"CFBD returned no games at all for season {season} week {week}; " +
                        "refusing to treat an empty payload as a schedule.");
                }

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

                        // A payload that omits the venue means "CFBD did not say", not "there is
                        // no venue" — keep what we already had.
                        existing.Venue = providerGame.Venue ?? existing.Venue;

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
            // Every string is truncated to its column length: CFBD is free to lengthen a name
            // at any time and a 101-character conference would otherwise fail the whole ingest.
            string name = Truncate(providerConference.Name, Conference.NameMaxLength);

            // Never null in the column, and empty for most non-FBS conferences; the read models
            // fall back to the name (D-171).
            string abbreviation = Truncate(
                providerConference.Abbreviation ?? string.Empty,
                Conference.AbbreviationMaxLength);

            if (existingByCfbdId.TryGetValue(providerConference.CfbdId, out Conference? existing))
            {
                existing.Name = name;
                existing.Abbreviation = abbreviation;
                existing.Classification = providerConference.Classification;
                ids[providerConference.CfbdId] = existing.Id;
            }
            else
            {
                var conference = new Conference
                {
                    Id = Guid.CreateVersion7(),
                    CfbdId = providerConference.CfbdId,
                    Name = name,
                    Abbreviation = abbreviation,
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

            // Truncated to the column lengths so a long provider string is stored short rather
            // than failing the whole ingest.
            string school = Truncate(providerTeam.School, Team.SchoolMaxLength);
            string? mascot = TruncateOptional(providerTeam.Mascot, Team.MascotMaxLength);
            string? abbreviation = TruncateOptional(providerTeam.Abbreviation, Team.AbbreviationMaxLength);
            string? logoUrl = TruncateOptional(providerTeam.LogoUrl, Team.LogoUrlMaxLength);

            if (existingByCfbdId.TryGetValue(providerTeam.CfbdId, out Team? existing))
            {
                existing.School = school;
                existing.ConferenceId = conferenceId ?? existing.ConferenceId;
                existing.Classification = providerTeam.Classification;

                // Optional fields: a payload that omits one means "CFBD did not say", not "clear
                // it". Only a value actually supplied overwrites what is stored.
                existing.Mascot = mascot ?? existing.Mascot;
                existing.Abbreviation = abbreviation ?? existing.Abbreviation;
                existing.LogoUrl = logoUrl ?? existing.LogoUrl;
                ids[providerTeam.CfbdId] = existing.Id;
            }
            else
            {
                var team = new Team
                {
                    Id = Guid.CreateVersion7(),
                    CfbdId = providerTeam.CfbdId,
                    School = school,
                    Mascot = mascot,
                    Abbreviation = abbreviation,
                    ConferenceId = conferenceId,
                    Classification = providerTeam.Classification,
                    LogoUrl = logoUrl,
                };
                _database.Teams.Add(team);
                ids[providerTeam.CfbdId] = team.Id;
            }
        }

        return ids;
    }

    /// <summary>
    /// Seeds <c>TeamAliases(Source=Cfbd)</c> from every team's <c>alternateNames</c>, first
    /// claimant wins (D-168).
    /// </summary>
    /// <remarks>
    /// <c>IX_TeamAliases_Source_Alias</c> is unique under the database's case-insensitive
    /// collation, so the claim map is keyed <see cref="StringComparer.OrdinalIgnoreCase"/> and
    /// spans the whole batch, not just the rows already on file: CFBD's real 2026 payload has
    /// 682 teams and hands the same alias text to more than one of them ("Tiffin"), and the same
    /// text in two casings ("Alma"/"ALMA"). Deduping only against existing rows - all P2-02 did -
    /// left the collisions inside one <c>SaveChangesAsync</c>, which SQL Server rejected and
    /// which therefore failed the entire teams ingest.
    /// </remarks>
    private async Task<int> UpsertAliasesAsync(
        IReadOnlyList<ProviderTeam> teams,
        Dictionary<int, Guid> teamIdsByCfbdId,
        CancellationToken cancellationToken)
    {
        List<TeamAlias> existingAliases = await _database.TeamAliases
            .Where(alias => alias.Source == ProviderSource.Cfbd)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Built by hand rather than ToDictionary: a database whose collation ever let two rows
        // differ only by case must not make the ingest throw on the way in.
        Dictionary<string, TeamAlias> claimedByAlias = new(StringComparer.OrdinalIgnoreCase);
        foreach (TeamAlias existing in existingAliases)
        {
            claimedByAlias.TryAdd(existing.Alias, existing);
        }

        int count = 0;
        List<string> collisions = [];

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

                string trimmedAlias = Truncate(alias.Trim(), TeamAlias.AliasMaxLength);

                // TeamNameIndex already indexes every team's own School, so an alias that only
                // repeats it buys the matcher nothing and would just be one more row competing
                // for the unique index (D-168).
                if (string.Equals(trimmedAlias, providerTeam.School, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (claimedByAlias.TryGetValue(trimmedAlias, out TeamAlias? claimed))
                {
                    // An alias already claimed - by a row on file, including P2-03's
                    // hand-verified TeamAliasSeed rows, or by an earlier team in this very batch
                    // - is left where it is. Repointing it would break the matcher silently.
                    if (claimed.TeamId != teamId)
                    {
                        collisions.Add($"{trimmedAlias} (claimed again by CFBD team {providerTeam.CfbdId})");
                        continue;
                    }
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
                    claimedByAlias[trimmedAlias] = row;
                }

                count++;
            }
        }

        if (collisions.Count > 0)
        {
            // One line, not one per alias: the real payload collides dozens of times and a log
            // entry each would bury the rest of the bootstrap.
            _logger.LogWarning(
                "Skipped {Count} CFBD alias(es) already claimed by another team; the first claimant keeps them. Examples: {Examples}",
                collisions.Count,
                string.Join("; ", collisions.Take(CollisionExamples)));
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

            _logger.LogInformation("{DataType} ingest succeeded: {Result}", dataType, result);

            return result;
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a cancelled request is not a provider failure: let it propagate so the
            // caller sees cancellation, and leave LastError describing whatever really went wrong
            // last. LastAttemptUtc was already committed above.
            throw;
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

    /// <summary>
    /// <see cref="Truncate(string,int)"/> for an optional provider string: null stays null, so
    /// "CFBD did not say" is still distinguishable from "CFBD said something long".
    /// </summary>
    private static string? TruncateOptional(string? value, int maxLength) =>
        value is null ? null : Truncate(value, maxLength);
}
