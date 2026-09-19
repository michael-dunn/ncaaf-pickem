using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;
using LeagueWeekDto = NcaafPickEm.Shared.Contracts.Seasons.LeagueWeek;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for leagues and memberships (Feature 01, P1-01). Endpoint handlers stay
/// thin: they resolve the membership through <c>RequireLeagueMember()</c>/<c>GetMembership()</c>
/// and call one method here. <see cref="Domain.Leagues.LeagueRules"/> owns the invariants; this
/// class owns persistence, the season calendar, and audit logging.
/// </summary>
public sealed class LeagueService
{
    /// <summary>Every new league starts with this point value per correct pick (P1-01 card).</summary>
    private const int DefaultPointValueOnCreate = 10;

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly SeasonCalendar _calendar;
    private readonly ISeasonWeekSource _weekSource;
    private readonly PointRuleService _pointRuleService;
    private readonly ILogger<LeagueService> _logger;

    /// <summary>Creates the service.</summary>
    public LeagueService(
        AppDbContext database,
        TimeProvider timeProvider,
        SeasonCalendar calendar,
        ISeasonWeekSource weekSource,
        PointRuleService pointRuleService,
        ILogger<LeagueService> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _calendar = calendar;
        _weekSource = weekSource;
        _pointRuleService = pointRuleService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a league. The creator becomes its sole Commissioner and an active member.
    /// </summary>
    public async Task<LeagueDetail> CreateAsync(
        Guid creatorUserId,
        CreateLeagueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = LeagueRules.ValidateName(request.Name);

        IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource.GetWeeksAsync(request.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        if (seasonWeeks.Count == 0)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidWeekRange,
                $"No season calendar is known for {request.SeasonYear}.");
        }

        LeagueWeekRange defaultRange = SeasonCalendar.DefaultLeagueRange(seasonWeeks);
        int firstWeek = request.FirstWeek ?? defaultRange.FirstWeek;
        int lastWeek = request.LastWeek ?? defaultRange.LastWeek;

        LeagueRules.ValidateWeekRange(firstWeek, lastWeek, seasonWeeks);

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        CurrentWeek currentWeek = SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), seasonWeeks);

        var league = new League
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            SeasonYear = request.SeasonYear,
            FirstWeek = firstWeek,
            LastWeek = lastWeek,
            DefaultPointValue = DefaultPointValueOnCreate,
            CreatedByUserId = creatorUserId,
            CreatedUtc = nowUtc,
            IsComplete = false,
        };

        var membership = new Membership
        {
            Id = Guid.CreateVersion7(),
            LeagueId = league.Id,
            UserId = creatorUserId,
            Role = MembershipRole.Commissioner,
            JoinedUtc = nowUtc,
            JoinedWeek = currentWeek.Week,
        };

        _database.Leagues.Add(league);
        _database.Memberships.Add(membership);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await BuildDetailAsync(league, membership, seasonWeeks, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every league the caller is an active member of.</summary>
    public async Task<LeagueSummary[]> ListMineAsync(Guid userId, CancellationToken cancellationToken)
    {
        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.League)
            .Where(membership => membership.UserId == userId && membership.RemovedUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (memberships.Count == 0)
        {
            return [];
        }

        Dictionary<int, IReadOnlyList<SeasonWeek>> weeksByYear = await LoadSeasonWeeksByYearAsync(
            memberships.Select(membership => membership.League!.SeasonYear), cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        var currentWeekByMembership = new int[memberships.Count];
        for (int i = 0; i < memberships.Count; i++)
        {
            League league = memberships[i].League!;
            IReadOnlyList<SeasonWeek> seasonWeeks = weeksByYear[league.SeasonYear];
            currentWeekByMembership[i] = seasonWeeks.Count == 0
                ? league.FirstWeek
                : SeasonCalendar.CurrentWeekAt(nowUtc, seasonWeeks).Week;
        }

        Dictionary<Guid, SubmissionStatus?> statusByMembershipId = await LoadCurrentWeekStatusesAsync(
            memberships, currentWeekByMembership, cancellationToken)
            .ConfigureAwait(false);

        var summaries = new LeagueSummary[memberships.Count];
        for (int i = 0; i < memberships.Count; i++)
        {
            Membership membership = memberships[i];
            League league = membership.League!;
            summaries[i] = new LeagueSummary(
                league.Id,
                league.Name,
                league.SeasonYear,
                membership.Role,
                currentWeekByMembership[i],
                statusByMembershipId[membership.Id]);
        }

        return summaries;
    }

    /// <summary>
    /// One <see cref="ISeasonWeekSource"/> call per distinct <c>SeasonYear</c> instead of one per
    /// membership/row (P1-01 review follow-up 1).
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<SeasonWeek>>> LoadSeasonWeeksByYearAsync(
        IEnumerable<int> seasonYears,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, IReadOnlyList<SeasonWeek>>();
        foreach (int year in seasonYears.Distinct())
        {
            result[year] = await _weekSource.GetWeeksAsync(year, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// One <c>WeekGameSets</c> query and one <c>WeekSubmissions</c> query for every membership's
    /// current week, instead of two queries per membership (P1-01 review follow-up 1).
    /// </summary>
    private async Task<Dictionary<Guid, SubmissionStatus?>> LoadCurrentWeekStatusesAsync(
        IReadOnlyList<Membership> memberships,
        int[] currentWeekByMembership,
        CancellationToken cancellationToken)
    {
        Guid[] leagueIds = [.. memberships.Select(membership => membership.LeagueId).Distinct()];

        Dictionary<(Guid LeagueId, int Week), Guid> gameSetIdByLeagueWeek = await _database.WeekGameSets
            .AsNoTracking()
            .Where(set => leagueIds.Contains(set.LeagueId))
            .ToDictionaryAsync(set => (set.LeagueId, set.Week), set => set.Id, cancellationToken)
            .ConfigureAwait(false);

        var weekGameSetIdByMembership = new Guid?[memberships.Count];
        var relevantSetIds = new HashSet<Guid>();
        for (int i = 0; i < memberships.Count; i++)
        {
            if (gameSetIdByLeagueWeek.TryGetValue((memberships[i].LeagueId, currentWeekByMembership[i]), out Guid setId))
            {
                weekGameSetIdByMembership[i] = setId;
                relevantSetIds.Add(setId);
            }
        }

        Guid[] membershipIds = [.. memberships.Select(membership => membership.Id)];
        Dictionary<Guid, SubmissionStatus> statusByMembershipId = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => membershipIds.Contains(submission.MembershipId) && relevantSetIds.Contains(submission.WeekGameSetId))
            .ToDictionaryAsync(submission => submission.MembershipId, submission => submission.Status, cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<Guid, SubmissionStatus?>();
        for (int i = 0; i < memberships.Count; i++)
        {
            Guid membershipId = memberships[i].Id;
            if (weekGameSetIdByMembership[i] is null)
            {
                // No game set for the current week: null, not "no game set" vs. NotStarted confusion.
                result[membershipId] = null;
                continue;
            }

            result[membershipId] = statusByMembershipId.TryGetValue(membershipId, out SubmissionStatus status)
                ? status
                : SubmissionStatus.NotStarted;
        }

        return result;
    }

    /// <summary>Full detail for a league the caller is already known to be a member of.</summary>
    public async Task<LeagueDetail> GetDetailAsync(Membership membership, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membership);

        League league = await _database.Leagues
            .FirstAsync(candidate => candidate.Id == membership.LeagueId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        return await BuildDetailAsync(league, membership, seasonWeeks, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates league settings. Commissioner only (enforced by the caller's route filter).</summary>
    public async Task<LeagueDetail> UpdateSettingsAsync(
        Membership membership,
        UpdateLeagueSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(request);

        League league = await _database.Leagues
            .FirstAsync(candidate => candidate.Id == membership.LeagueId, cancellationToken)
            .ConfigureAwait(false);

        string name = LeagueRules.ValidateName(request.Name);

        IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        LeagueRules.ValidateWeekRange(request.FirstWeek, request.LastWeek, seasonWeeks);

        if (request.DefaultPointValue is < League.MinPointValue or > League.MaxPointValue)
        {
            throw new LeagueRuleViolation(
                LeagueRuleViolationCode.InvalidPointValue,
                $"Default point value must be {League.MinPointValue} to {League.MaxPointValue}.");
        }

        bool defaultPointValueChanged = league.DefaultPointValue != request.DefaultPointValue;

        league.Name = name;
        league.FirstWeek = request.FirstWeek;
        league.LastWeek = request.LastWeek;
        league.DefaultPointValue = request.DefaultPointValue;

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The default feeds PointValueResolver's fallback (Feature 03, P3-03); every unlocked
        // week's ResolvedPointValue must reflect a changed default immediately, not on next
        // generate.
        if (defaultPointValueChanged)
        {
            await _pointRuleService.ReResolveUnlockedWeeksAsync(league.Id, cancellationToken).ConfigureAwait(false);
        }

        return await BuildDetailAsync(league, membership, seasonWeeks, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The league's roster. <paramref name="includeStatus"/> is true only for commissioner
    /// callers (03-API-Contracts.md). <paramref name="callerUserId"/> flags the caller's own row
    /// with <see cref="MemberRow.IsMe"/>.
    /// </summary>
    public async Task<MemberRow[]> GetMembersAsync(
        Guid leagueId,
        bool includeStatus,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        League league = await _database.Leagues
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == leagueId, cancellationToken)
            .ConfigureAwait(false);

        List<Membership> memberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.User)
            .Where(membership => membership.LeagueId == leagueId)
            .OrderBy(membership => membership.JoinedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, SubmissionStatus?> statusByMembershipId = [];
        if (includeStatus)
        {
            IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource
                .GetWeeksAsync(league.SeasonYear, cancellationToken)
                .ConfigureAwait(false);

            int currentWeek = seasonWeeks.Count == 0
                ? league.FirstWeek
                : SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), seasonWeeks).Week;

            List<Membership> activeMemberships = [.. memberships.Where(membership => membership.IsActive)];
            var currentWeekByMembership = new int[activeMemberships.Count];
            Array.Fill(currentWeekByMembership, currentWeek);

            statusByMembershipId = await LoadCurrentWeekStatusesAsync(
                activeMemberships, currentWeekByMembership, cancellationToken)
                .ConfigureAwait(false);
        }

        var rows = new MemberRow[memberships.Count];
        for (int i = 0; i < memberships.Count; i++)
        {
            Membership membership = memberships[i];

            SubmissionStatus? status = includeStatus && membership.IsActive
                ? statusByMembershipId.GetValueOrDefault(membership.Id)
                : null;

            rows[i] = new MemberRow(
                membership.Id,
                MemberNameProjection.Effective(membership),
                membership.Role,
                membership.JoinedWeek,
                IsFormer: !membership.IsActive,
                status,
                IsMe: membership.UserId == callerUserId);
        }

        return rows;
    }

    /// <summary>
    /// Sets or clears the caller's per-league display-name override. 409 (as
    /// <see cref="LeagueRuleViolation"/> with <see cref="LeagueRuleViolationCode.DisplayNameTaken"/>)
    /// when the effective name is already used by another active member.
    /// </summary>
    public async Task<MemberRow> SetDisplayNameAsync(
        Membership membership,
        SetLeagueDisplayNameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(request);

        string? normalized = LeagueRules.ValidateDisplayNameOverride(request.DisplayName);

        if (normalized is not null)
        {
            string normalizedUpper = normalized.ToUpperInvariant();

            bool taken = await _database.Memberships
                .AsNoTracking()
                .Where(m => m.LeagueId == membership.LeagueId && m.RemovedUtc == null && m.Id != membership.Id)
                .Select(MemberNameProjection.Selector)
                .AnyAsync(effectiveName => effectiveName.ToUpper() == normalizedUpper, cancellationToken)
                .ConfigureAwait(false);

            if (taken)
            {
                throw new LeagueRuleViolation(
                    LeagueRuleViolationCode.DisplayNameTaken,
                    "That display name is already used by another member of this league.");
            }
        }

        Membership tracked = await _database.Memberships
            .Include(m => m.User)
            .FirstAsync(m => m.Id == membership.Id, cancellationToken)
            .ConfigureAwait(false);

        tracked.DisplayNameOverride = normalized;
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MemberRow(
            tracked.Id,
            MemberNameProjection.Effective(tracked),
            tracked.Role,
            tracked.JoinedWeek,
            IsFormer: false,
            CurrentWeekStatus: null,
            IsMe: true);
    }

    /// <summary>
    /// Leagues where changing the caller's global (account-wide) display name to
    /// <paramref name="newDisplayName"/> would collide with another active member's effective
    /// name. Only leagues where the caller has no per-league override are checked, since an
    /// override already shields the global rename there. See DECISIONS.md (global-name
    /// collision rule, P1-03).
    /// </summary>
    public async Task<string[]> ListGlobalNameCollisionsAsync(
        Guid userId,
        string newDisplayName,
        CancellationToken cancellationToken)
    {
        string normalizedUpper = newDisplayName.ToUpperInvariant();

        List<Membership> myUnoverriddenMemberships = await _database.Memberships
            .AsNoTracking()
            .Include(membership => membership.League)
            .Where(membership => membership.UserId == userId
                && membership.RemovedUtc == null
                && membership.DisplayNameOverride == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var collisions = new List<string>();
        foreach (Membership mine in myUnoverriddenMemberships)
        {
            bool taken = await _database.Memberships
                .AsNoTracking()
                .Where(other => other.LeagueId == mine.LeagueId && other.RemovedUtc == null && other.Id != mine.Id)
                .Select(MemberNameProjection.Selector)
                .AnyAsync(effectiveName => effectiveName.ToUpper() == normalizedUpper, cancellationToken)
                .ConfigureAwait(false);

            if (taken)
            {
                collisions.Add(mine.League!.Name);
            }
        }

        return [.. collisions];
    }

    /// <summary>Soft-removes a member. 409 when the target is the caller or the last commissioner.</summary>
    public async Task RemoveMemberAsync(Membership caller, Guid targetMembershipId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        LeagueRules.EnsureNotActingOnSelf(caller.Id, targetMembershipId, "remove");

        Membership target = await LoadActiveMemberAsync(caller.LeagueId, targetMembershipId, cancellationToken)
            .ConfigureAwait(false);

        if (target.Role == MembershipRole.Commissioner)
        {
            int remaining = await ActiveCommissionerCountAsync(caller.LeagueId, excludeMembershipId: target.Id, cancellationToken)
                .ConfigureAwait(false);
            LeagueRules.EnsureAtLeastOneCommissionerRemains(remaining);
        }

        target.RemovedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        WriteAudit(caller, AuditAction.MemberRemoved, target.Id, new { targetMembershipId = target.Id });

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Membership {TargetMembershipId} removed from league {LeagueId} by {ActorMembershipId}",
            target.Id,
            caller.LeagueId,
            caller.Id);
    }

    /// <summary>Promotes an active member to Commissioner.</summary>
    public async Task PromoteAsync(Membership caller, Guid targetMembershipId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Membership target = await LoadActiveMemberAsync(caller.LeagueId, targetMembershipId, cancellationToken)
            .ConfigureAwait(false);

        MembershipRole before = target.Role;
        target.Role = MembershipRole.Commissioner;

        WriteAudit(caller, AuditAction.RolePromoted, target.Id, new { targetMembershipId = target.Id, before, after = target.Role });

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Membership {TargetMembershipId} promoted to Commissioner in league {LeagueId} by {ActorMembershipId}",
            target.Id,
            caller.LeagueId,
            caller.Id);
    }

    /// <summary>Demotes a commissioner to Member. 409 when the target is the last commissioner.</summary>
    public async Task DemoteAsync(Membership caller, Guid targetMembershipId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Membership target = await LoadActiveMemberAsync(caller.LeagueId, targetMembershipId, cancellationToken)
            .ConfigureAwait(false);

        if (target.Role == MembershipRole.Commissioner)
        {
            int remaining = await ActiveCommissionerCountAsync(caller.LeagueId, excludeMembershipId: target.Id, cancellationToken)
                .ConfigureAwait(false);
            LeagueRules.EnsureAtLeastOneCommissionerRemains(remaining);
        }

        MembershipRole before = target.Role;
        target.Role = MembershipRole.Member;

        WriteAudit(caller, AuditAction.RoleDemoted, target.Id, new { targetMembershipId = target.Id, before, after = target.Role });

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Membership {TargetMembershipId} demoted to Member in league {LeagueId} by {ActorMembershipId}",
            target.Id,
            caller.LeagueId,
            caller.Id);
    }

    /// <summary>Transfers the commissioner role: target promoted, caller demoted.</summary>
    public async Task TransferAsync(Membership caller, TransferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);

        Membership callerTracked = await _database.Memberships
            .FirstAsync(m => m.Id == caller.Id, cancellationToken)
            .ConfigureAwait(false);

        Membership target = await LoadActiveMemberAsync(caller.LeagueId, request.ToMembershipId, cancellationToken)
            .ConfigureAwait(false);

        LeagueRules.ApplyTransfer(callerTracked, target);

        WriteAudit(caller, AuditAction.RoleTransferred, target.Id, new { fromMembershipId = callerTracked.Id, toMembershipId = target.Id });

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Commissioner role transferred from {FromMembershipId} to {ToMembershipId} in league {LeagueId}",
            callerTracked.Id,
            target.Id,
            caller.LeagueId);
    }

    /// <summary>The league's playable weeks (First..Last) with their generation/lock state.</summary>
    public async Task<LeagueWeekDto[]> GetLeagueWeeksAsync(Membership membership, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membership);

        League league = await _database.Leagues
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == membership.LeagueId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        int currentWeek = seasonWeeks.Count == 0
            ? league.FirstWeek
            : SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), seasonWeeks).Week;

        Dictionary<int, WeekGameSet> gameSetsByWeek = await _database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.LeagueId == league.Id && set.Week >= league.FirstWeek && set.Week <= league.LastWeek)
            .ToDictionaryAsync(set => set.Week, cancellationToken)
            .ConfigureAwait(false);

        var weeks = new LeagueWeekDto[league.LastWeek - league.FirstWeek + 1];
        for (int week = league.FirstWeek; week <= league.LastWeek; week++)
        {
            gameSetsByWeek.TryGetValue(week, out WeekGameSet? set);

            weeks[week - league.FirstWeek] = new LeagueWeekDto(
                week,
                HasGameSet: set is not null,
                IsCurrent: week == currentWeek,
                IsLocked: set?.IsLocked ?? false,
                IsComplete: set?.IsComplete ?? false,
                LockAtUtc: set?.LockAtUtc is DateTime lockAt ? new DateTimeOffset(lockAt, TimeSpan.Zero) : null);
        }

        return weeks;
    }

    private async Task<LeagueDetail> BuildDetailAsync(
        League league,
        Membership membership,
        IReadOnlyList<SeasonWeek> seasonWeeks,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        int currentWeek = seasonWeeks.Count == 0
            ? league.FirstWeek
            : SeasonCalendar.CurrentWeekAt(nowUtc, seasonWeeks).Week;

        bool isComplete = seasonWeeks.Count > 0
            && SeasonCalendar.LeagueIsCompleteAt(nowUtc, league.LastWeek, seasonWeeks);

        if (isComplete != league.IsComplete)
        {
            // Keep the stored flag in sync opportunistically rather than trusting it alone
            // (P1-01 orchestrator guidance); a background job can take this over later.
            league.IsComplete = isComplete;
            await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        WeekGameSet? currentSet = await _database.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(set => set.LeagueId == league.Id && set.Week == currentWeek, cancellationToken)
            .ConfigureAwait(false);

        SubmissionStatus? status = currentSet is null
            ? null
            : await GetCurrentWeekStatusAsync(league.Id, membership.Id, currentWeek, cancellationToken)
                .ConfigureAwait(false);

        DateTimeOffset? lockAtUtc = currentSet?.LockAtUtc is DateTime lockAt
            ? new DateTimeOffset(lockAt, TimeSpan.Zero)
            : null;

        string? lockDisplay = lockAtUtc is DateTimeOffset lockValue
            ? SeasonCalendar.EasternDisplay(lockValue)
            : null;

        return new LeagueDetail(
            league.Id,
            league.Name,
            league.SeasonYear,
            league.FirstWeek,
            league.LastWeek,
            league.DefaultPointValue,
            currentWeek,
            isComplete,
            membership.Role,
            status,
            lockAtUtc,
            lockDisplay);
    }

    private async Task<SubmissionStatus?> GetCurrentWeekStatusAsync(
        Guid leagueId,
        Guid membershipId,
        int currentWeek,
        CancellationToken cancellationToken)
    {
        Guid? weekGameSetId = await _database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.LeagueId == leagueId && set.Week == currentWeek)
            .Select(set => (Guid?)set.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (weekGameSetId is null)
        {
            return null;
        }

        SubmissionStatus? status = await _database.WeekSubmissions
            .AsNoTracking()
            .Where(submission => submission.WeekGameSetId == weekGameSetId && submission.MembershipId == membershipId)
            .Select(submission => (SubmissionStatus?)submission.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // A game set exists but the member has not touched it yet: NotStarted, not "no game set".
        return status ?? SubmissionStatus.NotStarted;
    }

    private async Task<Membership> LoadActiveMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken)
    {
        Membership? membership = await _database.Memberships
            .FirstOrDefaultAsync(
                m => m.Id == membershipId && m.LeagueId == leagueId && m.RemovedUtc == null,
                cancellationToken)
            .ConfigureAwait(false);

        return membership ?? throw new LeagueRuleViolation(
            LeagueRuleViolationCode.MembershipNotFound,
            "No active member with that id in this league.");
    }

    private async Task<int> ActiveCommissionerCountAsync(Guid leagueId, Guid excludeMembershipId, CancellationToken cancellationToken)
    {
        return await _database.Memberships
            .CountAsync(
                m => m.LeagueId == leagueId
                    && m.RemovedUtc == null
                    && m.Role == MembershipRole.Commissioner
                    && m.Id != excludeMembershipId,
                cancellationToken)
            .ConfigureAwait(false);
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
}
