using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Engine.Views;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Reads the dashboard's <see cref="SimSnapshot"/>: the calendar as the app sees it, the current
/// week's games with their plans, the poll, and every league's set and members with this week's
/// picks and points. Read-only, so it runs outside the simulator's gate.
/// </summary>
public sealed class SnapshotReader
{
    private readonly AppDbContext _database;
    private readonly SimStateStore _store;
    private readonly CalendarDesk _calendar;
    private readonly SlateDesk _slate;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the reader over the step's scope.</summary>
    public SnapshotReader(
        AppDbContext database,
        SimStateStore store,
        CalendarDesk calendar,
        SlateDesk slate,
        TimeProvider timeProvider)
    {
        _database = database;
        _store = store;
        _calendar = calendar;
        _slate = slate;
        _timeProvider = timeProvider;
    }

    /// <summary>Reads the snapshot.</summary>
    public async Task<SimSnapshot> ReadAsync(int defaultSeasonYear, SimDatabaseStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        SimState state = await _store.LoadAsync(defaultSeasonYear, cancellationToken).ConfigureAwait(false);
        int season = state.SeasonYear;

        IReadOnlyList<SeasonWeek> weeks = await _calendar.ReadAsync(season, cancellationToken).ConfigureAwait(false);
        CurrentWeek? current = weeks.Count == 0 ? null : SeasonCalendar.CurrentWeekAt(nowUtc, weeks);

        Dictionary<int, int> gamesPerWeek = await _database.Games
            .AsNoTracking()
            .Where(game => game.SeasonYear == season)
            .GroupBy(game => game.Week)
            .Select(group => new { Week = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Week, row => row.Count, cancellationToken)
            .ConfigureAwait(false);

        var calendar = new SimCalendarView(
            [.. weeks.Select(week => new SimWeekView(
                week.Week,
                week.StartUtc,
                week.EndUtc,
                week.IsRegularSeason,
                gamesPerWeek.GetValueOrDefault(week.Week)))],
            current);

        var settings = new SimSettingsView(state.AutoPlay, state.GameMinutes, state.LockLeadMinutes, state.Slate);

        if (current is not CurrentWeek now)
        {
            return new SimSnapshot(nowUtc, status.DatabaseName, status.Problem, season, calendar, settings, state.TesterUserId, [], [], []);
        }

        int week = now.Week;
        Dictionary<Guid, int> ranks = await _slate.RanksAsync(season, week, cancellationToken).ConfigureAwait(false);
        List<SimGameView> games = await ReadGamesAsync(season, week, ranks, state, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> schools = await _database.Teams
            .AsNoTracking()
            .Where(team => ranks.Keys.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.School, cancellationToken)
            .ConfigureAwait(false);

        List<SimRankView> poll =
        [
            .. ranks
                .OrderBy(pair => pair.Value)
                .Select(pair => new SimRankView(pair.Value, pair.Key, schools.GetValueOrDefault(pair.Key, "?"))),
        ];

        List<SimLeagueView> leagues = await ReadLeaguesAsync(season, week, nowUtc, state.TesterUserId, cancellationToken).ConfigureAwait(false);

        return new SimSnapshot(nowUtc, status.DatabaseName, status.Problem, season, calendar, settings, state.TesterUserId, games, poll, leagues);
    }

    /// <summary>Every team, for the pickers: FBS by conference, then FCS.</summary>
    public async Task<IReadOnlyList<SimTeamOption>> ReadTeamsAsync(CancellationToken cancellationToken)
    {
        var teams = await _database.Teams
            .AsNoTracking()
            .Select(team => new
            {
                team.Id,
                team.School,
                Conference = team.Conference == null ? null : team.Conference.Abbreviation,
                team.Classification,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. teams
                .Select(team => new SimTeamOption(
                    team.Id,
                    team.School,
                    team.Classification == TeamClassification.Fbs ? team.Conference ?? "FBS" : "FCS"))
                .OrderBy(team => team.Conference == "FCS")
                .ThenBy(team => team.Conference, StringComparer.Ordinal)
                .ThenBy(team => team.School, StringComparer.Ordinal),
        ];
    }

    private async Task<List<SimGameView>> ReadGamesAsync(
        int season,
        int week,
        Dictionary<Guid, int> ranks,
        SimState state,
        CancellationToken cancellationToken)
    {
        var rows = await _database.Games
            .AsNoTracking()
            .Where(game => game.SeasonYear == season && game.Week == week)
            .OrderBy(game => game.KickoffUtc)
            .Select(game => new
            {
                Game = game,
                HomeSchool = game.HomeTeam!.School,
                HomeAbbreviation = game.HomeTeam.Abbreviation,
                HomeConference = game.HomeTeam.Conference == null ? null : game.HomeTeam.Conference.Abbreviation,
                HomeIsFbs = game.HomeTeam.Classification == TeamClassification.Fbs,
                AwaySchool = game.AwayTeam!.School,
                AwayAbbreviation = game.AwayTeam.Abbreviation,
                AwayConference = game.AwayTeam.Conference == null ? null : game.AwayTeam.Conference.Abbreviation,
                AwayIsFbs = game.AwayTeam.Classification == TeamClassification.Fbs,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] ids = [.. rows.Select(row => row.Game.Id)];
        Dictionary<Guid, decimal> spreads = await PointValueRecalculator
            .LoadLatestSpreadsAsync(_database, ids, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> setCounts = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => ids.Contains(row.GameId) && !row.IsRemoved)
            .GroupBy(row => row.GameId)
            .Select(group => new { GameId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.GameId, row => row.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new SimGameView(
                row.Game.Id,
                Utc(row.Game.KickoffUtc),
                new SimTeamView(row.Game.HomeTeamId, row.HomeSchool, row.HomeAbbreviation, Rank(ranks, row.Game.HomeTeamId), row.HomeConference, row.HomeIsFbs),
                new SimTeamView(row.Game.AwayTeamId, row.AwaySchool, row.AwayAbbreviation, Rank(ranks, row.Game.AwayTeamId), row.AwayConference, row.AwayIsFbs),
                spreads.TryGetValue(row.Game.Id, out decimal spread) ? spread : null,
                row.Game.IsConferenceGame,
                row.Game.IsSaturdayEastern,
                row.Game.Status,
                row.Game.HomeScore,
                row.Game.AwayScore,
                row.Game.Period,
                row.Game.Clock,
                state.Plans.GetValueOrDefault(row.Game.Id),
                setCounts.GetValueOrDefault(row.Game.Id))),
        ];
    }

    private async Task<List<SimLeagueView>> ReadLeaguesAsync(
        int season,
        int week,
        DateTimeOffset nowUtc,
        Guid? testerUserId,
        CancellationToken cancellationToken)
    {
        var leagues = await _database.Leagues
            .AsNoTracking()
            .Where(league => league.SeasonYear == season)
            .OrderBy(league => league.CreatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] leagueIds = [.. leagues.Select(league => league.Id)];

        List<WeekGameSet> sets = await _database.WeekGameSets
            .AsNoTracking()
            .Where(set => leagueIds.Contains(set.LeagueId) && set.Week == week)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid[] setIds = [.. sets.Select(set => set.Id)];

        List<WeekGameSetGame> setGames = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var members = await _database.Memberships
            .AsNoTracking()
            .Where(membership => leagueIds.Contains(membership.LeagueId) && membership.RemovedUtc == null)
            .Select(membership => new
            {
                membership.Id,
                membership.LeagueId,
                membership.UserId,
                Name = membership.DisplayNameOverride ?? membership.User!.DisplayName,
                Subject = membership.User!.ExternalSubject,
                membership.Role,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statuses = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .Select(row => new { row.MembershipId, row.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var picks = await _database.Picks
            .AsNoTracking()
            .Where(pick => setIds.Contains(pick.WeekGameSetGame!.WeekGameSetId))
            .Select(pick => new { pick.MembershipId, pick.WeekGameSetGame!.GameId, pick.PickedTeamId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var weekResults = await _database.WeekResults
            .AsNoTracking()
            .Where(row => setIds.Contains(row.WeekGameSetId))
            .Select(row => new { row.MembershipId, row.Points, row.CorrectCount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> seasonPoints = await _database.WeekResults
            .AsNoTracking()
            .Where(row => leagueIds.Contains(row.WeekGameSet!.LeagueId))
            .GroupBy(row => row.MembershipId)
            .Select(group => new { MembershipId = group.Key, Points = group.Sum(row => row.Points) })
            .ToDictionaryAsync(row => row.MembershipId, row => row.Points, cancellationToken)
            .ConfigureAwait(false);

        DateTime now = nowUtc.UtcDateTime;
        var views = new List<SimLeagueView>(leagues.Count);

        foreach (var league in leagues)
        {
            WeekGameSet? set = sets.FirstOrDefault(candidate => candidate.LeagueId == league.Id);
            SimSetView? setView = set is null
                ? null
                : new SimSetView(
                    set.Id,
                    UtcOrNull(set.LockAtUtc),
                    UtcOrNull(set.LockedUtc),
                    WeekGameSetLockGuard.IsFrozen(set, now),
                    set.IsComplete,
                    [
                        .. setGames
                            .Where(row => row.WeekGameSetId == set.Id)
                            .Select(row => new SimSetGameView(row.Id, row.GameId, row.ResolvedPointValue, row.Source, row.IsRemoved, row.IsVoided)),
                    ]);

            List<SimMemberView> memberViews =
            [
                .. members
                    .Where(member => member.LeagueId == league.Id)
                    .OrderByDescending(member => member.Role)
                    .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(member =>
                    {
                        var result = weekResults.FirstOrDefault(row => row.MembershipId == member.Id);
                        return new SimMemberView(
                            member.Id,
                            member.UserId,
                            member.Name,
                            LoginName(member.Subject),
                            member.Role,
                            member.UserId == testerUserId,
                            statuses.FirstOrDefault(row => row.MembershipId == member.Id)?.Status ?? SubmissionStatus.NotStarted,
                            picks.Where(pick => pick.MembershipId == member.Id).ToDictionary(pick => pick.GameId, pick => pick.PickedTeamId),
                            result?.Points,
                            result?.CorrectCount,
                            seasonPoints.GetValueOrDefault(member.Id));
                    }),
            ];

            views.Add(new SimLeagueView(league.Id, league.Name, league.FirstWeek, league.LastWeek, league.IsComplete, setView, memberViews));
        }

        return views;
    }

    /// <summary>The dev-login name for a simulator or fixture subject; null for anyone else.</summary>
    private static string? LoginName(string subject)
    {
        foreach (string prefix in (string[])[PeopleDesk.SubjectPrefix, "fixture:"])
        {
            if (subject.StartsWith(prefix, StringComparison.Ordinal))
            {
                return subject[prefix.Length..];
            }
        }

        return null;
    }

    private static int? Rank(Dictionary<Guid, int> ranks, Guid teamId) => ranks.TryGetValue(teamId, out int rank) ? rank : null;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? UtcOrNull(DateTime? value) => value is DateTime utc ? Utc(utc) : null;
}
