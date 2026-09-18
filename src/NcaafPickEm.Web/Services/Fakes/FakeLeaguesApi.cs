using Microsoft.AspNetCore.Components;
using NcaafPickEm.Shared.Contracts.Invites;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="ILeaguesApi"/> for Development, so P1-02 pages can be built and
/// screenshotted before P1-01's endpoints exist. See DECISIONS.md for the fake-API switch
/// (compile constant <c>USE_FAKE_API</c>, gated by <c>DEBUG</c> so it can never ship in Release).
/// </summary>
/// <remarks>
/// Seeds one sample league, "The Family League", season 2026, currently on week 7, with five
/// members (Michael, commissioner; Alyson, Dance, Alex, Daniel, members) and one active invite.
/// Visiting with <c>?emptyState=1</c> on the initial URL seeds no leagues at all, for the league
/// picker's empty-state screenshot. State lives for the lifetime of the WASM app instance (Blazor
/// WebAssembly has one DI scope for the whole session), so create/promote/invite actions here
/// persist as you navigate, exactly like the real API would.
/// </remarks>
public sealed class FakeLeaguesApi : ILeaguesApi
{
    /// <summary>The sample league's id, stable across a session for deep-linking in manual testing.</summary>
    public static readonly Guid SampleLeagueId = Guid.Parse("00000000-0000-0000-0000-00000000c0de");

    private static readonly Guid MichaelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AlysonId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DanceId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid AlexId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid DanielId = Guid.Parse("00000000-0000-0000-0000-000000000005");

    private readonly List<FakeMember> _members = [];
    private readonly List<FakeInvite> _invites = [];
    private FakeLeague? _league;
    private Guid _callerMembershipId = MichaelId;

    /// <summary>
    /// Seeds the sample league unless the page was opened with <c>?emptyState=1</c>. Opening with
    /// <c>?asMember=1</c> makes Alyson (a plain member) the caller instead of Michael the
    /// commissioner, for screenshotting the member-eye view of a page.
    /// </summary>
    /// <param name="navigation">Used only to read the query flags at startup.</param>
    public FakeLeaguesApi(NavigationManager navigation)
    {
        bool empty = navigation.Uri.Contains("emptyState=1", StringComparison.OrdinalIgnoreCase);
        if (!empty)
        {
            Seed();
        }

        if (navigation.Uri.Contains("asMember=1", StringComparison.OrdinalIgnoreCase))
        {
            _callerMembershipId = AlysonId;
        }
    }

    private void Seed()
    {
        _league = new FakeLeague
        {
            LeagueId = SampleLeagueId,
            Name = "The Family League",
            SeasonYear = 2026,
            FirstWeek = 1,
            LastWeek = 14,
            DefaultPointValue = 10,
            CurrentWeek = 7,
            LockAtUtc = new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero), // Sat 12:00 PM ET
        };

        _members.AddRange(
        [
            new FakeMember(MichaelId, "Michael", MembershipRole.Commissioner, 1, SubmissionStatus.Submitted),
            new FakeMember(AlysonId, "Alyson", MembershipRole.Member, 1, SubmissionStatus.Submitted),
            new FakeMember(DanceId, "Dance", MembershipRole.Member, 1, SubmissionStatus.NotStarted),
            new FakeMember(AlexId, "Alex", MembershipRole.Member, 3, SubmissionStatus.InProgress),
            new FakeMember(DanielId, "Daniel", MembershipRole.Member, 1, SubmissionStatus.NotStarted),
        ]);

        _invites.Add(new FakeInvite
        {
            InviteId = Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Code = "FAMILY7",
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14),
            Uses = 0,
            MaxUses = 50,
        });
    }

    /// <inheritdoc />
    public Task<LeagueSummary[]> GetMyLeaguesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_league is null
            ? Array.Empty<LeagueSummary>()
            : [ToSummary(_league, CallerMembership())]);

    /// <inheritdoc />
    public Task<LeagueDetail> CreateLeagueAsync(
        CreateLeagueRequest request,
        CancellationToken cancellationToken = default)
    {
        string name = request.Name.Trim();
        if (name.Length is 0 or > 50)
        {
            throw new LeaguesApiException(400, "League name must be 1 to 50 characters.");
        }

        _league = new FakeLeague
        {
            LeagueId = Guid.NewGuid(),
            Name = name,
            SeasonYear = request.SeasonYear,
            FirstWeek = request.FirstWeek ?? 1,
            LastWeek = request.LastWeek ?? 14,
            DefaultPointValue = 10,
            CurrentWeek = request.FirstWeek ?? 1,
            LockAtUtc = null,
        };
        _members.Clear();
        _members.Add(new FakeMember(MichaelId, "Michael", MembershipRole.Commissioner, _league.CurrentWeek, null));
        _callerMembershipId = MichaelId;

        return Task.FromResult(ToDetail(_league, CallerMembership()));
    }

    /// <inheritdoc />
    public Task<LeagueDetail> GetLeagueAsync(Guid leagueId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToDetail(RequireLeague(leagueId), CallerMembership()));

    /// <inheritdoc />
    public Task<LeagueDetail> UpdateSettingsAsync(
        Guid leagueId,
        UpdateLeagueSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        FakeLeague league = RequireLeague(leagueId);
        league.Name = request.Name.Trim();
        league.FirstWeek = request.FirstWeek;
        league.LastWeek = request.LastWeek;
        league.DefaultPointValue = request.DefaultPointValue;
        return Task.FromResult(ToDetail(league, CallerMembership()));
    }

    /// <inheritdoc />
    public Task<MemberRow[]> GetMembersAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        bool isCommish = CallerMembership()?.Role == MembershipRole.Commissioner;
        return Task.FromResult(_members
            .Select(m => new MemberRow(
                m.MembershipId,
                m.DisplayName,
                m.Role,
                m.JoinedWeek,
                m.IsFormer,
                isCommish ? m.CurrentWeekStatus : null))
            .ToArray());
    }

    /// <inheritdoc />
    public Task SetMyDisplayNameAsync(
        Guid leagueId,
        SetLeagueDisplayNameRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        FakeMember me = _members.First(m => m.MembershipId == _callerMembershipId);
        string? name = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (name is not null && _members.Any(m => m.MembershipId != me.MembershipId && m.DisplayName == name))
        {
            throw new LeaguesApiException(409, "That name is already taken in this league.");
        }

        me.DisplayName = name ?? me.DisplayName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        if (membershipId == _callerMembershipId)
        {
            throw new LeaguesApiException(409, "You cannot remove yourself.");
        }

        FakeMember target = RequireMember(membershipId);
        if (target.Role == MembershipRole.Commissioner
            && _members.Count(m => !m.IsFormer && m.Role == MembershipRole.Commissioner) <= 1)
        {
            throw new LeaguesApiException(409, "A league must keep at least one commissioner.");
        }

        target.IsFormer = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PromoteMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        RequireMember(membershipId).Role = MembershipRole.Commissioner;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DemoteMemberAsync(Guid leagueId, Guid membershipId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        FakeMember target = RequireMember(membershipId);
        if (_members.Count(m => !m.IsFormer && m.Role == MembershipRole.Commissioner) <= 1)
        {
            throw new LeaguesApiException(409, "A league must keep at least one commissioner.");
        }

        target.Role = MembershipRole.Member;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TransferCommissionerAsync(
        Guid leagueId,
        Guid toMembershipId,
        CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        FakeMember target = RequireMember(toMembershipId);
        FakeMember caller = _members.First(m => m.MembershipId == _callerMembershipId);
        target.Role = MembershipRole.Commissioner;
        caller.Role = MembershipRole.Member;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<InviteResponse> CreateInviteAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        var invite = new FakeInvite
        {
            InviteId = Guid.NewGuid(),
            Code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14),
            Uses = 0,
            MaxUses = 50,
        };
        _invites.Add(invite);
        return Task.FromResult(ToInviteResponse(invite));
    }

    /// <inheritdoc />
    public Task<InviteResponse[]> GetInvitesAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        return Task.FromResult(_invites.Select(ToInviteResponse).ToArray());
    }

    /// <inheritdoc />
    public Task RevokeInviteAsync(Guid leagueId, Guid inviteId, CancellationToken cancellationToken = default)
    {
        RequireLeague(leagueId);
        _invites.RemoveAll(i => i.InviteId == inviteId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<InvitePreview> GetInvitePreviewAsync(string code, CancellationToken cancellationToken = default)
    {
        if (_league is null)
        {
            return Task.FromResult(new InvitePreview(string.Empty, 0, 0, InviteState.Revoked));
        }

        FakeInvite? invite = _invites.FirstOrDefault(i =>
            string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

        InviteState state = invite is null
            ? InviteState.Revoked
            : invite.ExpiresUtc < DateTimeOffset.UtcNow
                ? InviteState.Expired
                : invite.Uses >= invite.MaxUses || _members.Count(m => !m.IsFormer) >= 50
                    ? InviteState.Full
                    : InviteState.Valid;

        return Task.FromResult(new InvitePreview(_league.Name, _league.SeasonYear, _members.Count(m => !m.IsFormer), state));
    }

    /// <inheritdoc />
    public Task<InviteAcceptOutcome> AcceptInviteAsync(string code, CancellationToken cancellationToken = default)
    {
        if (_league is null)
        {
            return Task.FromResult(new InviteAcceptOutcome(
                null,
                new InvitePreview(string.Empty, 0, 0, InviteState.Revoked)));
        }

        FakeInvite? invite = _invites.FirstOrDefault(i =>
            string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
        if (invite is null || invite.ExpiresUtc < DateTimeOffset.UtcNow)
        {
            return Task.FromResult(new InviteAcceptOutcome(
                null,
                new InvitePreview(_league.Name, _league.SeasonYear, _members.Count(m => !m.IsFormer), InviteState.Expired)));
        }

        var newMember = new FakeMember(Guid.NewGuid(), "New member", MembershipRole.Member, _league.CurrentWeek, null);
        _members.Add(newMember);
        invite.Uses++;
        return Task.FromResult(new InviteAcceptOutcome(ToDetail(_league, newMember), null));
    }

    private FakeMember? CallerMembership() => _members.FirstOrDefault(m => m.MembershipId == _callerMembershipId);

    private FakeLeague RequireLeague(Guid leagueId) =>
        _league?.LeagueId == leagueId ? _league : throw new LeaguesApiException(404, "League not found.");

    private FakeMember RequireMember(Guid membershipId) =>
        _members.FirstOrDefault(m => m.MembershipId == membershipId)
        ?? throw new LeaguesApiException(404, "Member not found.");

    private static LeagueSummary ToSummary(FakeLeague league, FakeMember? me) => new(
        league.LeagueId,
        league.Name,
        league.SeasonYear,
        me?.Role ?? MembershipRole.Member,
        league.CurrentWeek,
        me?.CurrentWeekStatus);

    private static LeagueDetail ToDetail(FakeLeague league, FakeMember? me) => new(
        league.LeagueId,
        league.Name,
        league.SeasonYear,
        league.FirstWeek,
        league.LastWeek,
        league.DefaultPointValue,
        league.CurrentWeek,
        IsComplete: league.CurrentWeek > league.LastWeek,
        me?.Role ?? MembershipRole.Member,
        me?.CurrentWeekStatus,
        league.LockAtUtc,
        league.LockAtUtc is null ? null : "Sat 12:00 PM ET");

    private static InviteResponse ToInviteResponse(FakeInvite invite) => new(
        invite.InviteId,
        invite.Code,
        $"https://pickem.example/join/{invite.Code}",
        invite.ExpiresUtc,
        invite.Uses,
        invite.MaxUses);

    private sealed class FakeLeague
    {
        public required Guid LeagueId { get; set; }
        public required string Name { get; set; }
        public required int SeasonYear { get; set; }
        public required int FirstWeek { get; set; }
        public required int LastWeek { get; set; }
        public required int DefaultPointValue { get; set; }
        public required int CurrentWeek { get; set; }
        public DateTimeOffset? LockAtUtc { get; set; }
    }

    private sealed class FakeMember(
        Guid membershipId,
        string displayName,
        MembershipRole role,
        int joinedWeek,
        SubmissionStatus? currentWeekStatus)
    {
        public Guid MembershipId { get; } = membershipId;
        public string DisplayName { get; set; } = displayName;
        public MembershipRole Role { get; set; } = role;
        public int JoinedWeek { get; } = joinedWeek;
        public bool IsFormer { get; set; }
        public SubmissionStatus? CurrentWeekStatus { get; set; } = currentWeekStatus;
    }

    private sealed class FakeInvite
    {
        public required Guid InviteId { get; set; }
        public required string Code { get; set; }
        public required DateTimeOffset ExpiresUtc { get; set; }
        public int Uses { get; set; }
        public required int MaxUses { get; set; }
    }
}
