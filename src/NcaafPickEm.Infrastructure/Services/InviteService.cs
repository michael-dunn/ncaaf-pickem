using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Application service for shareable join codes (Feature 01, P1-01).
/// </summary>
public sealed class InviteService
{
    private const int MaxCodeCollisionRetries = 5;

    private readonly AppDbContext _database;
    private readonly TimeProvider _timeProvider;
    private readonly ISeasonWeekSource _weekSource;
    private readonly LeagueService _leagueService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InviteService> _logger;

    /// <summary>Creates the service.</summary>
    public InviteService(
        AppDbContext database,
        TimeProvider timeProvider,
        ISeasonWeekSource weekSource,
        LeagueService leagueService,
        IConfiguration configuration,
        ILogger<InviteService> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _weekSource = weekSource;
        _leagueService = leagueService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Creates a new active invite for the caller's league.</summary>
    /// <param name="commissioner">The creating commissioner's membership.</param>
    /// <param name="fallbackOrigin">
    /// Origin to build <c>Url</c> from when <c>App:PublicOrigin</c> is not configured
    /// (the request's own origin).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InviteResponse> CreateAsync(
        Membership commissioner,
        string fallbackOrigin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commissioner);

        string code = await GenerateUniqueCodeAsync(cancellationToken).ConfigureAwait(false);
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var invite = new Invite
        {
            Id = Guid.CreateVersion7(),
            LeagueId = commissioner.LeagueId,
            Code = code,
            CreatedByMembershipId = commissioner.Id,
            ExpiresUtc = nowUtc + Invite.DefaultLifetime,
            MaxUses = Invite.DefaultMaxUses,
            Uses = 0,
        };

        _database.Invites.Add(invite);
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToResponse(invite, ResolveOrigin(fallbackOrigin));
    }

    /// <summary>Every active (not revoked, not expired, not exhausted) invite for the league.</summary>
    public async Task<InviteResponse[]> ListActiveAsync(
        Guid leagueId,
        string fallbackOrigin,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        List<Invite> invites = await _database.Invites
            .AsNoTracking()
            .Where(invite => invite.LeagueId == leagueId
                && invite.RevokedUtc == null
                && invite.ExpiresUtc > nowUtc
                && invite.Uses < invite.MaxUses)
            .OrderByDescending(invite => invite.ExpiresUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string origin = ResolveOrigin(fallbackOrigin);
        return [.. invites.Select(invite => ToResponse(invite, origin))];
    }

    /// <summary>
    /// <c>App:PublicOrigin</c> when configured, else the caller-supplied fallback (the current
    /// request's own origin).
    /// </summary>
    private string ResolveOrigin(string fallbackOrigin) =>
        _configuration["App:PublicOrigin"] is { Length: > 0 } configured ? configured : fallbackOrigin;

    /// <summary>Revokes an invite. Returns false when no such invite exists in this league.</summary>
    public async Task<bool> RevokeAsync(Guid leagueId, Guid inviteId, CancellationToken cancellationToken)
    {
        Invite? invite = await _database.Invites
            .FirstOrDefaultAsync(candidate => candidate.Id == inviteId && candidate.LeagueId == leagueId, cancellationToken)
            .ConfigureAwait(false);

        if (invite is null)
        {
            return false;
        }

        invite.RevokedUtc ??= _timeProvider.GetUtcNow().UtcDateTime;
        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Preview of an invite for a signed-in caller. Null when the code does not exist.</summary>
    public async Task<InvitePreview?> PreviewAsync(string code, Guid callerUserId, CancellationToken cancellationToken)
    {
        (Invite invite, League league, InviteState state, int memberCount)? resolved =
            await ResolveAsync(code, callerUserId, cancellationToken).ConfigureAwait(false);

        if (resolved is null)
        {
            return null;
        }

        (_, League league, InviteState state, int memberCount) = resolved.Value;
        return new InvitePreview(league.Name, league.SeasonYear, memberCount, state);
    }

    /// <summary>
    /// Accepts an invite for the caller. Reactivates a previously removed membership rather than
    /// creating a second row (the unique index on <c>(LeagueId, UserId)</c> forbids a duplicate);
    /// see DECISIONS.md.
    /// </summary>
    public async Task<InviteAcceptResult?> AcceptAsync(string code, Guid callerUserId, CancellationToken cancellationToken)
    {
        (Invite invite, League league, InviteState state, int memberCount)? resolved =
            await ResolveAsync(code, callerUserId, cancellationToken).ConfigureAwait(false);

        if (resolved is null)
        {
            return null;
        }

        (Invite invite, League league, InviteState state, int memberCount) = resolved.Value;

        if (state != InviteState.Valid)
        {
            return new InviteAcceptResult(null, new InvitePreview(league.Name, league.SeasonYear, memberCount, state));
        }

        IReadOnlyList<SeasonWeek> seasonWeeks = await _weekSource
            .GetWeeksAsync(league.SeasonYear, cancellationToken)
            .ConfigureAwait(false);

        int currentWeek = seasonWeeks.Count == 0
            ? league.FirstWeek
            : SeasonCalendar.CurrentWeekAt(_timeProvider.GetUtcNow(), seasonWeeks).Week;

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        Membership? existing = await _database.Memberships
            .FirstOrDefaultAsync(m => m.LeagueId == league.Id && m.UserId == callerUserId, cancellationToken)
            .ConfigureAwait(false);

        Membership membership;
        if (existing is not null)
        {
            // A former member re-joining: reactivate the same row rather than inserting a second
            // one for the same (LeagueId, UserId) pair.
            existing.RemovedUtc = null;
            existing.Role = MembershipRole.Member;
            existing.JoinedUtc = nowUtc;
            existing.JoinedWeek = currentWeek;
            membership = existing;
        }
        else
        {
            membership = new Membership
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                UserId = callerUserId,
                Role = MembershipRole.Member,
                JoinedUtc = nowUtc,
                JoinedWeek = currentWeek,
            };
            _database.Memberships.Add(membership);
        }

        invite.Uses += 1;

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "User {UserId} joined league {LeagueId} via invite {InviteId} (membership {MembershipId})",
            callerUserId,
            league.Id,
            invite.Id,
            membership.Id);

        var detail = await _leagueService.GetDetailAsync(membership, cancellationToken).ConfigureAwait(false);
        return new InviteAcceptResult(detail, null);
    }

    private async Task<(Invite Invite, League League, InviteState State, int MemberCount)?> ResolveAsync(
        string code,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        Invite? invite = await _database.Invites
            .Include(candidate => candidate.League)
            .FirstOrDefaultAsync(candidate => candidate.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (invite is null)
        {
            return null;
        }

        League league = invite.League!;
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        int memberCount = await _database.Memberships
            .CountAsync(m => m.LeagueId == league.Id && m.RemovedUtc == null, cancellationToken)
            .ConfigureAwait(false);

        bool alreadyMember = await _database.Memberships
            .AnyAsync(m => m.LeagueId == league.Id && m.UserId == callerUserId && m.RemovedUtc == null, cancellationToken)
            .ConfigureAwait(false);

        InviteState state = ResolveState(invite, nowUtc, alreadyMember, memberCount);
        return (invite, league, state, memberCount);
    }

    private static InviteState ResolveState(Invite invite, DateTime nowUtc, bool alreadyMember, int memberCount)
    {
        if (invite.RevokedUtc is not null)
        {
            return InviteState.Revoked;
        }

        if (nowUtc > invite.ExpiresUtc)
        {
            return InviteState.Expired;
        }

        if (alreadyMember)
        {
            return InviteState.AlreadyMember;
        }

        bool full = memberCount >= LeagueRules.MemberCap || invite.Uses >= invite.MaxUses;
        return full ? InviteState.Full : InviteState.Valid;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxCodeCollisionRetries; attempt++)
        {
            string candidate = InviteCodeGenerator.Generate();
            bool exists = await _database.Invites
                .AnyAsync(invite => invite.Code == candidate, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique invite code.");
    }

    private static InviteResponse ToResponse(Invite invite, string fallbackOrigin)
    {
        string origin = fallbackOrigin.TrimEnd('/');
        string url = $"{origin}/join/{invite.Code}";

        return new InviteResponse(
            invite.Id,
            invite.Code,
            url,
            new DateTimeOffset(invite.ExpiresUtc, TimeSpan.Zero),
            invite.Uses,
            invite.MaxUses);
    }
}
