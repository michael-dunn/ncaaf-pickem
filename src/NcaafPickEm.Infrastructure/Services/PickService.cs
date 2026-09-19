using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for weekly picks (Feature 04, P4-01): reading a member's own week, setting
/// and submitting picks, the post-lock all-picks view, and the commissioner's status roster.
/// <see cref="SubmissionStatusCalculator"/> owns the status rules; this class owns persistence,
/// the current-week and lock guards, and the DTO shapes.
/// </summary>
/// <remarks>
/// Games come from <see cref="GameSetService.GetWeekGameSetAsync"/> so ranks, point values and
/// <c>WinnerTeamId</c> are derived in exactly one place (<see cref="GameSetGameDtoMapper"/>).
/// P4-02's lock job writes <see cref="SubmissionStatus.Locked"/>/<see cref="SubmissionStatus.Incomplete"/>;
/// every recompute here is skipped once <c>WeekGameSets.LockedUtc</c> is set, so those survive.
/// </remarks>
public sealed class PickService
{
    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly ISeasonWeekSource _weekSource;
    private readonly GameSetService _gameSetService;
    private readonly ILogger<PickService> _logger;

    /// <summary>Creates the service.</summary>
    public PickService(
        AppDbContext database,
        TimeProvider timeProvider,
        ISeasonWeekSource weekSource,
        GameSetService gameSetService,
        ILogger<PickService> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _weekSource = weekSource;
        _gameSetService = gameSetService;
        _logger = logger;
    }

    /// <summary>
    /// The caller's own picks for a week. Works for any week inside the league's range: a past or
    /// locked week simply comes back with <see cref="MyPicksResponse.IsLocked"/> set, which is the
    /// client's cue to render it read-only.
    /// </summary>
    /// <param name="caller">The caller's membership, from <c>HttpContext.GetMembership()</c>.</param>
    /// <param name="week">The week to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    public async Task<MyPicksResponse> GetMyPicksAsync(
        Membership caller,
        int week,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        WeekGameSetResponse games = await _gameSetService
            .GetWeekGameSetAsync(caller.LeagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await LoadSetAsync(caller.LeagueId, week, cancellationToken).ConfigureAwait(false);
        if (set is null)
        {
            return new MyPicksResponse(week, SubmissionStatus.NotStarted, null, null, false, 0, 0, false, []);
        }

        return await BuildMyPicksAsync(caller, week, set, games, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets (or changes) the caller's pick on one game. Tapping the team they already picked is a
    /// no-op success. Recomputes their week status and returns the whole page state, so the client
    /// never needs a second round trip.
    /// </summary>
    /// <param name="caller">The caller's membership.</param>
    /// <param name="week">The week; must be the current week.</param>
    /// <param name="gameId">The <c>Games.Id</c> of the game inside the week's set.</param>
    /// <param name="teamId">The team being picked; must be home or away of that game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    /// <exception cref="PickRuleViolation">
    /// The week is not current (409), is locked (409), the game is not in the set (404) or not
    /// active (409), or the team is not in the game (400).
    /// </exception>
    public async Task<MyPicksResponse> SetPickAsync(
        Membership caller,
        int week,
        Guid gameId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        WeekGameSet set = await EnsurePickableWeekAsync(caller.LeagueId, week, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSetGame row = await _database.WeekGameSetGames
            .Include(candidate => candidate.Game)
            .FirstOrDefaultAsync(
                candidate => candidate.WeekGameSetId == set.Id && candidate.GameId == gameId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PickRuleViolation(
                PickRuleViolationCode.GameNotInSet,
                "That game is not part of this week's set.");

        if (!row.IsActive)
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.GameNotActive,
                "That game has been taken out of this week's set and takes no picks.");
        }

        Game game = row.Game!;
        if (teamId != game.HomeTeamId && teamId != game.AwayTeamId)
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.TeamNotInGame,
                "That team is not playing in that game.");
        }

        Pick? pick = await _database.Picks
            .FirstOrDefaultAsync(
                candidate => candidate.MembershipId == caller.Id && candidate.WeekGameSetGameId == row.Id,
                cancellationToken)
            .ConfigureAwait(false);

        bool changed;
        if (pick is null)
        {
            _database.Picks.Add(new Pick
            {
                Id = Guid.CreateVersion7(),
                MembershipId = caller.Id,
                WeekGameSetGameId = row.Id,
                PickedTeamId = teamId,
                UpdatedUtc = nowUtc,
            });
            changed = true;
        }
        else if (pick.PickedTeamId == teamId)
        {
            // Tapping the already-picked team never unpicks it (Feature 04) and writes nothing.
            changed = false;
        }
        else
        {
            pick.PickedTeamId = teamId;
            pick.UpdatedUtc = nowUtc;
            changed = true;
        }

        if (changed)
        {
            await ApplyStatusAsync(set, caller.Id, extraPickedGameSetGameId: row.Id, nowUtc, cancellationToken)
                .ConfigureAwait(false);
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetMyPicksAsync(caller, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Submits the caller's week. Refused while any active game is unpicked. Idempotent: a second
    /// submit leaves <c>SubmittedUtc</c> alone.
    /// </summary>
    /// <param name="caller">The caller's membership.</param>
    /// <param name="week">The week; must be the current week.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    /// <exception cref="PickRuleViolation">
    /// The week is not current or is locked (409), the set holds no active games (409), or picks
    /// are missing (409, with the missing count in <see cref="PickRuleViolation.Count"/>).
    /// </exception>
    public async Task<MyPicksResponse> SubmitAsync(
        Membership caller,
        int week,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        WeekGameSet set = await EnsurePickableWeekAsync(caller.LeagueId, week, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ActiveSetGame> active = await LoadActiveGamesAsync(set.Id, cancellationToken)
            .ConfigureAwait(false);

        if (active.Count == 0)
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.NoGamesInSet,
                "This week has no games to submit yet.");
        }

        IReadOnlyCollection<Guid> picked = await LoadPickedGameSetGameIdsAsync(set.Id, caller.Id, cancellationToken)
            .ConfigureAwait(false);

        WeekSubmission submission = await GetOrCreateSubmissionAsync(set.Id, caller.Id, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        SubmissionState state = SubmissionStatusCalculator.Calculate(active, picked, submission.SubmittedUtc);

        if (state.PickedCount < state.TotalCount)
        {
            int missing = state.TotalCount - state.PickedCount;
            throw new PickRuleViolation(
                PickRuleViolationCode.IncompletePicks,
                $"{missing} of {state.TotalCount} games still need a pick.",
                missing);
        }

        if (state.Status != SubmissionStatus.Submitted)
        {
            submission.SubmittedUtc = nowUtc;
            submission.LastChangedUtc = nowUtc;
            submission.Status = SubmissionStatus.Submitted;

            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Membership {MembershipId} submitted week {Week} picks in league {LeagueId} ({PickedCount} games).",
                caller.Id,
                week,
                caller.LeagueId,
                state.PickedCount);
        }

        return await GetMyPicksAsync(caller, week, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the caller's "games changed since you last looked" flag for a week. A no-op when the
    /// caller has no submission row yet.
    /// </summary>
    /// <param name="caller">The caller's membership.</param>
    /// <param name="week">The week.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    public async Task AckChangesAsync(Membership caller, int week, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        await WeekRangeGuard.EnsureWeekInRangeAsync(_database, caller.LeagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await LoadSetAsync(caller.LeagueId, week, cancellationToken).ConfigureAwait(false);
        if (set is null)
        {
            return;
        }

        WeekSubmission? submission = await _database.WeekSubmissions
            .FirstOrDefaultAsync(
                candidate => candidate.WeekGameSetId == set.Id && candidate.MembershipId == caller.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (submission is null || !submission.HasUnseenGameChanges)
        {
            return;
        }

        submission.HasUnseenGameChanges = false;
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Every member's picks for a locked week. Refused with 403 until the lock job has run, so no
    /// member can see another's picks early (Feature 04, Visibility).
    /// </summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="week">The week.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    /// <exception cref="PickRuleViolation">The week has not locked yet (403).</exception>
    public async Task<WeekPicksResponse> GetAllPicksAsync(
        Guid leagueId,
        int week,
        CancellationToken cancellationToken)
    {
        WeekGameSetResponse games = await _gameSetService
            .GetWeekGameSetAsync(leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await LoadSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);

        if (set?.LockedUtc is null)
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.PicksNotVisible,
                "Everyone's picks become visible when the week locks.");
        }

        GameSetGameDto[] activeGames = [.. games.Games.Where(game => !game.IsVoided)];
        HashSet<Guid> activeIds = [.. activeGames.Select(game => game.GameSetGameId!.Value)];

        // A WeekSubmissions row the lock job settled is the record of who was in the league at
        // lock, so it — not the current roster — decides whose column appears, former members
        // included. The status clause is the whole rule (D-135): this service creates a row the
        // moment a member first touches the week and D-111 leaves it behind when the locker drops
        // the membership, so "any row at all" would also column a member who picked and then left
        // before the week locked. Locked/Incomplete are the only two values LockWeekJob writes.
        Dictionary<Guid, SubmissionStatus> statuses = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == set.Id
                && (submission.Status == SubmissionStatus.Locked
                    || submission.Status == SubmissionStatus.Incomplete))
            .ToDictionaryAsync(submission => submission.MembershipId, submission => submission.Status, cancellationToken)
            .ConfigureAwait(false);

        Guid[] membershipIds = [.. statuses.Keys];

        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membershipIds.Contains(membership.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<Pick>> picksByMembership = await LoadPicksByMembershipAsync(
            activeIds, membershipIds, cancellationToken).ConfigureAwait(false);

        MemberPicksRow[] members = [.. memberships
            .Select(membership => new
            {
                membership.Id,
                DisplayName = MemberNameProjection.Effective(membership),
            })
            .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                Dictionary<Guid, Guid> picked = picksByMembership.TryGetValue(row.Id, out List<Pick>? list)
                    ? list.ToDictionary(pick => pick.WeekGameSetGameId, pick => pick.PickedTeamId)
                    : [];

                MemberPickDto[] picks = [.. activeGames.Select(game => new MemberPickDto(
                    game.GameSetGameId!.Value,
                    picked.TryGetValue(game.GameSetGameId!.Value, out Guid teamId) ? teamId : null))];

                return new MemberPicksRow(row.Id, row.DisplayName, statuses[row.Id], picks);
            })];

        return new WeekPicksResponse(activeGames, members);
    }

    /// <summary>
    /// The commissioner's roster of who has submitted for a week: every active membership, with
    /// <see cref="SubmissionStatus.NotStarted"/> for anyone who has not started.
    /// </summary>
    /// <param name="leagueId">The league.</param>
    /// <param name="week">The week.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GameSetRuleViolation">The week is outside the league's range (404).</exception>
    public async Task<MemberStatusRow[]> GetStatusRosterAsync(
        Guid leagueId,
        int week,
        CancellationToken cancellationToken)
    {
        await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == leagueId && membership.RemovedUtc == null)
            .OrderBy(membership => membership.JoinedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        WeekGameSet? set = await LoadSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false);

        if (set is null)
        {
            return [.. memberships.Select(membership => new MemberStatusRow(
                membership.Id,
                MemberNameProjection.Effective(membership),
                SubmissionStatus.NotStarted,
                0,
                0))];
        }

        IReadOnlyList<ActiveSetGame> active = await LoadActiveGamesAsync(set.Id, cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> activeIds = [.. active.Select(game => game.GameSetGameId)];

        Dictionary<Guid, List<Pick>> picksByMembership = await LoadPicksByMembershipAsync(
            activeIds, [.. memberships.Select(membership => membership.Id)], cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, WeekSubmission> submissions = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == set.Id)
            .ToDictionaryAsync(submission => submission.MembershipId, cancellationToken)
            .ConfigureAwait(false);

        var result = new MemberStatusRow[memberships.Count];
        for (int i = 0; i < memberships.Count; i++)
        {
            Membership membership = memberships[i];
            submissions.TryGetValue(membership.Id, out WeekSubmission? submission);

            Guid[] picked = picksByMembership.TryGetValue(membership.Id, out List<Pick>? list)
                ? [.. list.Select(pick => pick.WeekGameSetGameId)]
                : [];

            SubmissionState state = SubmissionStatusCalculator.Calculate(active, picked, submission?.SubmittedUtc);

            // After the lock job has run, its Locked/Incomplete verdict is the truth.
            SubmissionStatus status = set.LockedUtc is null
                ? state.Status
                : submission?.Status ?? SubmissionStatus.NotStarted;

            result[i] = new MemberStatusRow(
                membership.Id,
                MemberNameProjection.Effective(membership),
                status,
                state.PickedCount,
                state.TotalCount);
        }

        return result;
    }

    /// <summary>
    /// Recomputes one member's status row for a week and saves. P4-04's <c>GameAddedToSet</c> /
    /// <c>GameRemovedFromSet</c> handlers call this after the set changes.
    /// </summary>
    /// <param name="weekGameSetId">The week's set.</param>
    /// <param name="membershipId">The member.</param>
    /// <param name="markUnseenGameChanges">
    /// True to also raise <c>HasUnseenGameChanges</c>, so the picks page highlights what changed.
    /// Never lowers the flag; only <c>ack-changes</c> does that.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// A locked week is left untouched, so the lock job's <see cref="SubmissionStatus.Locked"/> /
    /// <see cref="SubmissionStatus.Incomplete"/> survive. A member with no row and nothing to say
    /// (status <see cref="SubmissionStatus.NotStarted"/>, no flag to raise) gets no row written.
    /// </remarks>
    public async Task RecomputeStatusAsync(
        Guid weekGameSetId,
        Guid membershipId,
        bool markUnseenGameChanges,
        CancellationToken cancellationToken)
    {
        bool changed = await RecomputeCoreAsync(
            weekGameSetId,
            [membershipId],
            markUnseenGameChanges ? [membershipId] : null,
            cancellationToken).ConfigureAwait(false) > 0;

        if (changed)
        {
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Recomputes every existing status row for a week's set and saves. The bulk form P4-04 uses
    /// when a game is added to or removed from the set.
    /// </summary>
    /// <param name="weekGameSetId">The week's set.</param>
    /// <param name="markUnseenGameChangesFor">
    /// Memberships whose <c>HasUnseenGameChanges</c> flag should be raised, or null for none.
    /// A membership named here gets a row created if it does not have one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows were written.</returns>
    /// <remarks>A locked week is left untouched (see <see cref="RecomputeStatusAsync"/>).</remarks>
    public async Task<int> RecomputeWeekStatusesAsync(
        Guid weekGameSetId,
        IReadOnlyCollection<Guid>? markUnseenGameChangesFor,
        CancellationToken cancellationToken)
    {
        List<Guid> existing = await _database.WeekSubmissions
            .Where(submission => submission.WeekGameSetId == weekGameSetId)
            .Select(submission => submission.MembershipId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> targets = [.. existing];
        if (markUnseenGameChangesFor is not null)
        {
            targets.UnionWith(markUnseenGameChangesFor);
        }

        int written = await RecomputeCoreAsync(weekGameSetId, targets, markUnseenGameChangesFor, cancellationToken)
            .ConfigureAwait(false);

        if (written > 0)
        {
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    private async Task<int> RecomputeCoreAsync(
        Guid weekGameSetId,
        IReadOnlyCollection<Guid> membershipIds,
        IReadOnlyCollection<Guid>? markUnseenGameChangesFor,
        CancellationToken cancellationToken)
    {
        if (membershipIds.Count == 0)
        {
            return 0;
        }

        WeekGameSet? set = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == weekGameSetId, cancellationToken)
            .ConfigureAwait(false);

        if (set is null || set.LockedUtc is not null)
        {
            return 0;
        }

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<ActiveSetGame> active = await LoadActiveGamesAsync(weekGameSetId, cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> activeIds = [.. active.Select(game => game.GameSetGameId)];

        Dictionary<Guid, List<Pick>> picksByMembership = await LoadPicksByMembershipAsync(
            activeIds, membershipIds, cancellationToken).ConfigureAwait(false);

        List<WeekSubmission> rows = await _database.WeekSubmissions
            .Where(submission => submission.WeekGameSetId == weekGameSetId
                && membershipIds.Contains(submission.MembershipId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, WeekSubmission> rowsByMembership = rows.ToDictionary(row => row.MembershipId);
        HashSet<Guid> flagged = markUnseenGameChangesFor is null ? [] : [.. markUnseenGameChangesFor];

        int written = 0;
        foreach (Guid membershipId in membershipIds)
        {
            Guid[] picked = picksByMembership.TryGetValue(membershipId, out List<Pick>? list)
                ? [.. list.Select(pick => pick.WeekGameSetGameId)]
                : [];

            rowsByMembership.TryGetValue(membershipId, out WeekSubmission? submission);
            SubmissionState state = SubmissionStatusCalculator.Calculate(active, picked, submission?.SubmittedUtc);

            bool raiseFlag = flagged.Contains(membershipId);

            if (submission is null)
            {
                if (state.Status == SubmissionStatus.NotStarted && !raiseFlag)
                {
                    continue;
                }

                submission = NewSubmission(weekGameSetId, membershipId, nowUtc);
                _database.WeekSubmissions.Add(submission);
            }

            submission.Status = state.Status;
            submission.LastChangedUtc = nowUtc;
            if (raiseFlag)
            {
                submission.HasUnseenGameChanges = true;
            }

            written++;
        }

        return written;
    }

    private async Task<MyPicksResponse> BuildMyPicksAsync(
        Membership caller,
        int week,
        WeekGameSet set,
        WeekGameSetResponse games,
        CancellationToken cancellationToken)
    {
        var rows = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == set.Id && !row.IsRemoved)
            .Select(row => new { row.Id, row.AddedUtc, row.IsVoided })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] rowIds = [.. rows.Select(row => row.Id)];

        Dictionary<Guid, Guid> myPicks = await _database.Picks
            .AsNoTracking()
            .Where(pick => pick.MembershipId == caller.Id && rowIds.Contains(pick.WeekGameSetGameId))
            .ToDictionaryAsync(pick => pick.WeekGameSetGameId, pick => pick.PickedTeamId, cancellationToken)
            .ConfigureAwait(false);

        WeekSubmission? submission = await _database.WeekSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.WeekGameSetId == set.Id && candidate.MembershipId == caller.Id,
                cancellationToken)
            .ConfigureAwait(false);

        ActiveSetGame[] active = [.. rows
            .Where(row => !row.IsVoided)
            .Select(row => new ActiveSetGame(row.Id, row.AddedUtc))];

        SubmissionState state = SubmissionStatusCalculator.Calculate(active, myPicks.Keys, submission?.SubmittedUtc);

        SubmissionStatus status = set.LockedUtc is null
            ? state.Status
            : submission?.Status ?? SubmissionStatus.NotStarted;

        Dictionary<Guid, DateTime> addedByRow = rows.ToDictionary(row => row.Id, row => row.AddedUtc);

        MyPickGameDto[] dtos = [.. games.Games.Select(game =>
        {
            Guid gameSetGameId = game.GameSetGameId!.Value;
            bool isNew = submission?.SubmittedUtc is DateTime submittedUtc
                && addedByRow.TryGetValue(gameSetGameId, out DateTime addedUtc)
                && addedUtc > submittedUtc;

            return new MyPickGameDto(
                game,
                myPicks.TryGetValue(gameSetGameId, out Guid teamId) ? teamId : null,
                isNew);
        })];

        return new MyPicksResponse(
            week,
            status,
            games.LockAtUtc,
            games.LockAtEasternDisplay,
            IsLockedNow(set, _timeProvider.GetUtcNow().UtcDateTime),
            state.PickedCount,
            state.TotalCount,
            submission?.HasUnseenGameChanges ?? false,
            dtos);
    }

    /// <summary>
    /// The three guards every mutation runs, in the order the contract documents them: the week is
    /// one the league plays (404), it is the current week (409), and it is not locked (409).
    /// </summary>
    private async Task<WeekGameSet> EnsurePickableWeekAsync(
        Guid leagueId,
        int week,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        League league = await WeekRangeGuard.EnsureWeekInRangeAsync(_database, leagueId, week, cancellationToken)
            .ConfigureAwait(false);

        await EnsureCurrentWeekAsync(league, week, cancellationToken).ConfigureAwait(false);

        WeekGameSet set = await LoadSetAsync(leagueId, week, cancellationToken).ConfigureAwait(false)
            ?? throw new PickRuleViolation(
                PickRuleViolationCode.GameNotInSet,
                "This week has no game set yet.");

        if (IsLockedNow(set, nowUtc))
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.Locked,
                "Picks for this week are locked.");
        }

        return set;
    }

    private async Task EnsureCurrentWeekAsync(League league, int week, CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        if (seasonWeeks.Count == 0)
        {
            throw new PickRuleViolation(
                PickRuleViolationCode.WeekNotCurrent,
                $"No season calendar is known for {league.SeasonYear}, so no week is pickable.");
        }

        CurrentWeek current = SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), seasonWeeks);
        if (current.Week == week)
        {
            return;
        }

        string reason = week < current.Week
            ? $"Week {week} is over; picks are only accepted for the current week."
            : $"Week {week} has not started; picks are only accepted for the current week.";

        throw new PickRuleViolation(PickRuleViolationCode.WeekNotCurrent, $"{reason} The current week is {current.Week}.");
    }

    /// <summary>
    /// Picks stop the moment the first game kicks off, whether or not the lock job has run yet
    /// (04-Domain-Algorithms.md section 4: reject when <c>nowUtc &gt;= LockAtUtc</c> or
    /// <c>LockedUtc != null</c>). Since D-110 the game-set and point-value services refuse on
    /// exactly the same condition, so both go through <see cref="WeekGameSetLockGuard"/>.
    /// </summary>
    private static bool IsLockedNow(WeekGameSet set, DateTime nowUtc) =>
        WeekGameSetLockGuard.IsFrozen(set, nowUtc);

    private async Task ApplyStatusAsync(
        WeekGameSet set,
        Guid membershipId,
        Guid extraPickedGameSetGameId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ActiveSetGame> active = await LoadActiveGamesAsync(set.Id, cancellationToken)
            .ConfigureAwait(false);

        // The pick just made is still only in the change tracker, so add it by hand.
        HashSet<Guid> picked = [.. await LoadPickedGameSetGameIdsAsync(set.Id, membershipId, cancellationToken)
            .ConfigureAwait(false), extraPickedGameSetGameId];

        WeekSubmission submission = await GetOrCreateSubmissionAsync(set.Id, membershipId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        SubmissionState state = SubmissionStatusCalculator.Calculate(active, picked, submission.SubmittedUtc);

        submission.Status = state.Status;
        submission.LastChangedUtc = nowUtc;
    }

    private async Task<WeekSubmission> GetOrCreateSubmissionAsync(
        Guid weekGameSetId,
        Guid membershipId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        WeekSubmission? submission = await _database.WeekSubmissions
            .FirstOrDefaultAsync(
                candidate => candidate.WeekGameSetId == weekGameSetId && candidate.MembershipId == membershipId,
                cancellationToken)
            .ConfigureAwait(false);

        if (submission is not null)
        {
            return submission;
        }

        submission = NewSubmission(weekGameSetId, membershipId, nowUtc);
        _database.WeekSubmissions.Add(submission);
        return submission;
    }

    private static WeekSubmission NewSubmission(Guid weekGameSetId, Guid membershipId, DateTime nowUtc) => new()
    {
        WeekGameSetId = weekGameSetId,
        MembershipId = membershipId,
        Status = SubmissionStatus.NotStarted,
        LastChangedUtc = nowUtc,
        HasUnseenGameChanges = false,
    };

    private async Task<WeekGameSet?> LoadSetAsync(Guid leagueId, int week, CancellationToken cancellationToken) =>
        await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(set => set.LeagueId == leagueId && set.Week == week, cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<ActiveSetGame>> LoadActiveGamesAsync(
        Guid weekGameSetId,
        CancellationToken cancellationToken)
    {
        var rows = await _database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == weekGameSetId && !row.IsRemoved && !row.IsVoided)
            .Select(row => new { row.Id, row.AddedUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new ActiveSetGame(row.Id, row.AddedUtc))];
    }

    private async Task<IReadOnlyCollection<Guid>> LoadPickedGameSetGameIdsAsync(
        Guid weekGameSetId,
        Guid membershipId,
        CancellationToken cancellationToken) =>
        await _database.Picks
            .AsNoTracking()
            .Where(pick => pick.MembershipId == membershipId
                && pick.WeekGameSetGame!.WeekGameSetId == weekGameSetId)
            .Select(pick => pick.WeekGameSetGameId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<Guid, List<Pick>>> LoadPicksByMembershipAsync(
        IReadOnlyCollection<Guid> gameSetGameIds,
        IReadOnlyCollection<Guid> membershipIds,
        CancellationToken cancellationToken)
    {
        if (gameSetGameIds.Count == 0 || membershipIds.Count == 0)
        {
            return [];
        }

        List<Pick> picks = await _database.Picks
            .AsNoTracking()
            .Where(pick => membershipIds.Contains(pick.MembershipId)
                && gameSetGameIds.Contains(pick.WeekGameSetGameId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return picks
            .GroupBy(pick => pick.MembershipId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }
}
