using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Dev;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Seeding;

/// <summary>
/// Walks the seeded demo league through the week the clock currently points at (P10-01): generate
/// the set, fill every member's picks, lock, poll a fixture snapshot, or wipe the league's week
/// data and start again. What <c>SimulateCommand</c> does offline, exposed to a running
/// Development server behind <c>/api/admin/fixture/demo</c> so the same steps can be driven from
/// the browser while the <c>DevTimeProvider</c> moves time.
/// </summary>
/// <remarks>
/// Every step goes through the real services (<see cref="GameSetService"/>,
/// <see cref="PickService"/>, <see cref="LockWeekJob"/>, <see cref="SaturdayPoller.PollOnceAsync"/>),
/// so the same rules apply as for a member in the app: picks are refused once the week is
/// locked, generate is refused once the set is frozen, and so on. Refusals come back as notes
/// rather than errors, because the point of the tool is to see where the week stands.
/// Registered in Development and Testing only.
/// </remarks>
public sealed class DemoWeekService
{
    private readonly AppDbContext _database;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;
    private readonly ISeasonWeekSource _weekSource;
    private readonly GameSetService _gameSets;
    private readonly PickService _picks;
    private readonly FixtureSnapshotState _snapshotState;
    private readonly ILogger<DemoWeekService> _logger;

    /// <summary>Creates the service. Scoped; every dependency but the clock and snapshot state is scoped too.</summary>
    public DemoWeekService(
        AppDbContext database,
        IServiceProvider services,
        TimeProvider timeProvider,
        ISeasonWeekSource weekSource,
        GameSetService gameSets,
        PickService picks,
        FixtureSnapshotState snapshotState,
        ILogger<DemoWeekService> logger)
    {
        _database = database;
        _services = services;
        _timeProvider = timeProvider;
        _weekSource = weekSource;
        _gameSets = gameSets;
        _picks = picks;
        _snapshotState = snapshotState;
        _logger = logger;
    }

    /// <summary>Where the demo league stands this week, or null when it has not been seeded.</summary>
    public async Task<DemoWeekResponse?> GetAsync(CancellationToken cancellationToken)
    {
        DemoContext? context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);
        return context is null ? null : await BuildResponseAsync(context, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the week's set if needed and generates it from the league's default rules, the way
    /// <c>EnsureCurrentWeekSetsJob</c> would on Sunday morning. Refused (as a note) once the week is frozen.
    /// </summary>
    public async Task<DemoWeekResponse?> GenerateAsync(CancellationToken cancellationToken)
    {
        DemoContext? context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var notes = new List<string>();

        if (!context.HasFixtureGames)
        {
            notes.Add(
                $"generate: the fixture data has no games in week {context.Week}; move the clock into " +
                $"week {FixtureReferenceDataProvider.FixtureWeek} first");
            return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
        }

        await EnsureDefaultRulesAsync(context.League, notes, cancellationToken).ConfigureAwait(false);

        try
        {
            await _gameSets.GetOrCreateWeekSetAsync(context.League.Id, context.Week, cancellationToken).ConfigureAwait(false);
            WeekGameSetResponse response = await _gameSets
                .GenerateAsync(context.League.Id, context.Week, cancellationToken)
                .ConfigureAwait(false);
            notes.Add($"generate: {response.Games.Length} game(s) in the week {context.Week} set");
        }
        catch (GameSetRuleViolation violation)
        {
            notes.Add($"generate: skipped ({violation.Code}: {violation.Message})");
        }

        return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes a pick on every unpicked active game for every active member and submits, through
    /// <see cref="PickService"/> so the lock and current-week rules apply. The favourite (better
    /// rank, else home) is picked, with every member after the first flipping a different third
    /// of the games so the leaderboard is not a five-way tie.
    /// </summary>
    /// <param name="excludeUserId">A user to leave alone, so the tester can still pick by hand.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<DemoWeekResponse?> FillPicksAsync(Guid? excludeUserId, CancellationToken cancellationToken)
    {
        DemoContext? context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var notes = new List<string>();

        List<Membership> members = await _database.Memberships
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == context.League.Id && membership.RemovedUtc == null)
            .OrderByDescending(membership => membership.Role)
            .ThenBy(membership => membership.User!.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int variant = 0;
        foreach (Membership membership in members)
        {
            int memberVariant = variant++;
            string name = MemberName(membership);

            if (membership.UserId == excludeUserId)
            {
                notes.Add($"picks: {name} skipped (that is you)");
                continue;
            }

            try
            {
                MyPicksResponse mine = await _picks.GetMyPicksAsync(membership, context.Week, cancellationToken).ConfigureAwait(false);
                if (mine.TotalCount == 0)
                {
                    notes.Add("picks: the week has no active games; generate the set first");
                    break;
                }

                int made = 0;
                for (int index = 0; index < mine.Games.Length; index++)
                {
                    MyPickGameDto row = mine.Games[index];
                    if (row.Game.IsVoided || row.MyTeamId is not null)
                    {
                        continue;
                    }

                    Guid teamId = ChooseTeam(row.Game, index, memberVariant);
                    await _picks.SetPickAsync(membership, context.Week, row.Game.GameId, teamId, cancellationToken).ConfigureAwait(false);
                    made++;
                }

                SubmissionStatus status = mine.Status;
                if (made > 0 || status != SubmissionStatus.Submitted)
                {
                    MyPicksResponse after = await _picks.SubmitAsync(membership, context.Week, cancellationToken).ConfigureAwait(false);
                    status = after.Status;
                }

                notes.Add($"picks: {name} made {made} pick(s), status {status}");
            }
            catch (PickRuleViolation violation)
            {
                notes.Add($"picks: {name} stopped ({violation.Code}: {violation.Message})");
                if (violation.Code is PickRuleViolationCode.Locked or PickRuleViolationCode.WeekNotCurrent)
                {
                    break;
                }
            }
        }

        return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <see cref="LockWeekJob"/> on this week's set now, whatever the clock says - the same
    /// switch <c>simulate --lock</c> throws. Moving the clock past <c>LockAtUtc</c> and waiting a
    /// minute for the scheduler does the same thing the natural way.
    /// </summary>
    public async Task<DemoWeekResponse?> LockNowAsync(CancellationToken cancellationToken)
    {
        DemoContext? context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var notes = new List<string>();

        WeekGameSet? set = await LoadSetAsync(context, cancellationToken).ConfigureAwait(false);
        if (set is null || set.LockAtUtc is null)
        {
            notes.Add("lock: nothing to lock (no set, or the set has no active games)");
        }
        else if (set.LockedUtc is DateTime lockedUtc)
        {
            notes.Add($"lock: already locked at {ToOffset(lockedUtc):o}");
        }
        else
        {
            LockWeekJob job = ActivatorUtilities.CreateInstance<LockWeekJob>(_services);
            await job.RunAsync(
                new OneShotOccurrence(set.Id.ToString("N"), ToOffset(set.LockAtUtc.Value)),
                cancellationToken).ConfigureAwait(false);
            notes.Add("lock: LockWeekJob ran; picks are frozen and point values are settled");
        }

        return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Optionally moves the fixture score timeline to <paramref name="snapshot"/>, then polls once
    /// for the week's Saturday exactly as <see cref="SaturdayPoller"/> would inside its window:
    /// scores land on the games, finals raise <c>GameWentFinal</c>, and the week is scored.
    /// </summary>
    /// <param name="snapshot">1 (pre-kickoff) through 6 (all Final), or null to keep the current one.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task<DemoWeekResponse?> PollAsync(int? snapshot, CancellationToken cancellationToken)
    {
        DemoContext? context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var notes = new List<string>();

        if (snapshot is int requested)
        {
            if (requested < FixtureSnapshotState.MinSnapshot || requested > FixtureSnapshotState.MaxSnapshot)
            {
                notes.Add(
                    $"poll: snapshot must be between {FixtureSnapshotState.MinSnapshot} and " +
                    $"{FixtureSnapshotState.MaxSnapshot}; nothing was polled");
                return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
            }

            _snapshotState.Set(requested);
        }

        SeasonWeek? seasonWeek = context.Weeks.FirstOrDefault(candidate => candidate.Week == context.Week);
        DateOnly saturday = seasonWeek is null
            ? DateOnly.FromDateTime(SeasonCalendar.ToEastern(context.NowUtc).DateTime)
            : DateOnly.FromDateTime(SeasonCalendar.ToEastern(seasonWeek.EndUtc).DateTime);

        LiveScoreApplyResult result = await SaturdayPoller.PollOnceAsync(
            _services,
            _database,
            _timeProvider,
            saturday,
            _logger,
            cancellationToken).ConfigureAwait(false);

        notes.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"poll: snapshot {_snapshotState.Current} for {saturday:yyyy-MM-dd} (Eastern): "
                + $"{result.Matched} matched, {result.Changed} changed, {result.Unmatched} unmatched, "
                + $"{result.Events.Count} event(s)"));

        return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes every week-scoped row the demo league owns (results, picks, submissions, sets and
    /// their games, standings snapshots), puts the fixture games back to Scheduled with no score,
    /// and rewinds the snapshot to 1. Members, rules and the league itself stay.
    /// </summary>
    public async Task<DemoWeekResponse?> ResetAsync(CancellationToken cancellationToken)
    {
        DemoContext? context = await LoadContextAsync(cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        Guid leagueId = context.League.Id;

        int results = await _database.WeekResults
            .Where(row => row.WeekGameSet!.LeagueId == leagueId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int picks = await _database.Picks
            .Where(row => row.WeekGameSetGame!.WeekGameSet!.LeagueId == leagueId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int submissions = await _database.WeekSubmissions
            .Where(row => row.WeekGameSet!.LeagueId == leagueId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int setGames = await _database.WeekGameSetGames
            .Where(row => row.WeekGameSet!.LeagueId == leagueId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int sets = await _database.WeekGameSets
            .Where(row => row.LeagueId == leagueId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int standings = await _database.SeasonStandingsSnapshots
            .Where(row => row.LeagueId == leagueId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        int games = await _database.Games
            .Where(game => game.SeasonYear == FixtureReferenceDataProvider.FixtureSeason
                && game.Week == FixtureReferenceDataProvider.FixtureWeek)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(game => game.Status, GameStatus.Scheduled)
                    .SetProperty(game => game.HomeScore, (int?)null)
                    .SetProperty(game => game.AwayScore, (int?)null)
                    .SetProperty(game => game.Period, (byte?)null)
                    .SetProperty(game => game.Clock, (string?)null)
                    .SetProperty(game => game.LastScoreUpdateUtc, (DateTime?)null),
                cancellationToken).ConfigureAwait(false);

        _snapshotState.Reset();

        _logger.LogInformation(
            "Demo league reset: {Sets} set(s), {SetGames} set game(s), {Picks} pick(s), {Submissions} submission(s), " +
            "{Results} result(s), {Standings} standings row(s) deleted; {Games} fixture game(s) back to Scheduled",
            sets,
            setGames,
            picks,
            submissions,
            results,
            standings,
            games);

        var notes = new List<string>
        {
            $"reset: deleted {sets} set(s), {setGames} set game(s), {picks} pick(s), {submissions} submission(s), "
            + $"{results} result(s), {standings} standings row(s); {games} fixture game(s) back to Scheduled; snapshot 1",
        };

        return await BuildResponseAsync(context, notes, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDefaultRulesAsync(League league, List<string> notes, CancellationToken cancellationToken)
    {
        bool hasRules = await _database.GameSetRules
            .AnyAsync(rule => rule.LeagueId == league.Id && rule.Week == null, cancellationToken)
            .ConfigureAwait(false);

        if (hasRules)
        {
            return;
        }

        GameSetRuleDto[] rules = [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];
        await _gameSets.ReplaceDefaultRulesAsync(league.Id, rules, cancellationToken).ConfigureAwait(false);
        notes.Add("rules: seeded the default [Top 25] rule (the league had none)");
    }

    /// <summary>
    /// The favourite - the better-ranked side, an unranked side losing to a ranked one, home on a
    /// tie - flipped on every third game for members after the first, staggered by member.
    /// </summary>
    private static Guid ChooseTeam(GameSetGameDto game, int gameIndex, int memberVariant)
    {
        bool homeIsFavourite = (game.HomeRank, game.AwayRank) switch
        {
            (int home, int away) => home <= away,
            (int, null) => true,
            (null, int) => false,
            (null, null) => true,
        };

        bool flip = memberVariant > 0 && (gameIndex + memberVariant) % 3 == 0;
        bool pickHome = homeIsFavourite != flip;

        return pickHome ? game.HomeTeam.TeamId : game.AwayTeam.TeamId;
    }

    private async Task<DemoContext?> LoadContextAsync(CancellationToken cancellationToken)
    {
        League? league = await _database.Leagues
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Name == FixtureSeeder.DemoLeagueName, cancellationToken)
            .ConfigureAwait(false);

        if (league is null)
        {
            return null;
        }

        IReadOnlyList<SeasonWeek> weeks = await _weekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        CurrentWeek current = weeks.Count == 0
            ? new CurrentWeek(league.FirstWeek, SeasonState.BeforeSeason)
            : SeasonCalendar.CurrentWeekAt(nowUtc, weeks);
        int week = Math.Clamp(current.Week, league.FirstWeek, league.LastWeek);

        bool hasFixtureGames = await _database.Games
            .AnyAsync(game => game.SeasonYear == league.SeasonYear && game.Week == week, cancellationToken)
            .ConfigureAwait(false);

        return new DemoContext(league, weeks, current, week, nowUtc, hasFixtureGames);
    }

    private Task<WeekGameSet?> LoadSetAsync(DemoContext context, CancellationToken cancellationToken) =>
        _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.LeagueId == context.League.Id && candidate.Week == context.Week,
                cancellationToken);

    private async Task<DemoWeekResponse> BuildResponseAsync(
        DemoContext context,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        WeekGameSet? set = await LoadSetAsync(context, cancellationToken).ConfigureAwait(false);

        DemoWeekSetDto? setDto = null;
        Dictionary<Guid, SubmissionStatus> statuses = [];
        Dictionary<Guid, int> pickCounts = [];
        Dictionary<Guid, (int Points, int Correct)> results = [];

        if (set is not null)
        {
            var games = await _database.WeekGameSetGames
                .AsNoTracking()
                .Where(row => row.WeekGameSetId == set.Id && !row.IsRemoved)
                .Select(row => new { row.IsVoided, row.Game!.Status })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            setDto = new DemoWeekSetDto(
                set.Week,
                games.Count,
                games.Count(game => !game.IsVoided),
                games.Count(game => !game.IsVoided && game.Status == GameStatus.Final),
                games.Count(game => !game.IsVoided && game.Status == GameStatus.InProgress),
                ToOffset(set.LockAtUtc),
                set.LockAtUtc is DateTime lockAt ? SeasonCalendar.EasternDisplay(ToOffset(lockAt)) : null,
                ToOffset(set.LockedUtc),
                WeekGameSetLockGuard.IsFrozen(set, context.NowUtc.UtcDateTime),
                set.IsComplete);

            Guid setId = set.Id;

            statuses = await _database.WeekSubmissions
                .AsNoTracking()
                .Where(row => row.WeekGameSetId == setId)
                .ToDictionaryAsync(row => row.MembershipId, row => row.Status, cancellationToken)
                .ConfigureAwait(false);

            pickCounts = await _database.Picks
                .AsNoTracking()
                .Where(row => row.WeekGameSetGame!.WeekGameSetId == setId && !row.WeekGameSetGame.IsRemoved)
                .GroupBy(row => row.MembershipId)
                .Select(group => new { MembershipId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.MembershipId, row => row.Count, cancellationToken)
                .ConfigureAwait(false);

            results = await _database.WeekResults
                .AsNoTracking()
                .Where(row => row.WeekGameSetId == setId)
                .ToDictionaryAsync(row => row.MembershipId, row => (row.Points, row.CorrectCount), cancellationToken)
                .ConfigureAwait(false);
        }

        var members = await _database.Memberships
            .AsNoTracking()
            .Where(membership => membership.LeagueId == context.League.Id && membership.RemovedUtc == null)
            .Select(membership => new
            {
                membership.Id,
                // MemberNameProjection.Selector inlined; it cannot be composed into an anonymous projection.
                Name = membership.DisplayNameOverride
                    ?? (membership.User != null ? membership.User.DisplayName : string.Empty),
                membership.Role,
            })
            .OrderByDescending(membership => membership.Role)
            .ThenBy(membership => membership.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DemoMemberDto[] memberDtos =
        [
            .. members.Select(member => new DemoMemberDto(
                member.Name,
                member.Role.ToString(),
                (statuses.TryGetValue(member.Id, out SubmissionStatus status) ? status : SubmissionStatus.NotStarted).ToString(),
                pickCounts.TryGetValue(member.Id, out int picked) ? picked : 0,
                results.TryGetValue(member.Id, out (int Points, int Correct) result) ? result.Points : null,
                results.TryGetValue(member.Id, out result) ? result.Correct : null))
        ];

        return new DemoWeekResponse(
            context.League.Id,
            context.League.Name,
            context.NowUtc,
            context.Week,
            context.Current.State.ToString(),
            context.HasFixtureGames,
            _snapshotState.Current,
            setDto,
            memberDtos,
            [.. notes]);
    }

    private static string MemberName(Membership membership) =>
        membership.DisplayNameOverride ?? membership.User?.DisplayName ?? membership.UserId.ToString("N");

    private static DateTimeOffset ToOffset(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? utc) => utc is DateTime value ? ToOffset(value) : null;

    /// <summary>The demo league and where the clock puts it, loaded once per call.</summary>
    private sealed record DemoContext(
        League League,
        IReadOnlyList<SeasonWeek> Weeks,
        CurrentWeek Current,
        int Week,
        DateTimeOffset NowUtc,
        bool HasFixtureGames);
}
