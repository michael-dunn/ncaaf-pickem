using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Picks;

/// <summary>
/// Week lock (Features 04 and 05, <c>04-Domain-Algorithms.md</c> section 5 steps 1 and 2): the
/// frozen spread and point value per active game, and the Locked/Incomplete verdict per member.
/// </summary>
public sealed class WeekLockerTests
{
    private const int DefaultPointValue = 5;

    private static readonly DateTime SetBuilt = new(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LockAt = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);

    private static readonly Guid GameA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GameB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GameC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid Michael = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Alyson = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Dance = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static readonly Guid HomeTeam = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid AwayTeam = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Conference = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    [Fact]
    public void GivenASubmittedMember_WhenLocking_ThenTheyAreLocked()
    {
        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA), Game(GameB)],
            [Member(Michael, [GameA, GameB], submittedUtc: LockAt.AddHours(-2))]));

        result.Members.Should().ContainSingle()
            .Which.Should().Be(new LockedMemberStatus(Michael, SubmissionStatus.Locked));
    }

    [Fact]
    public void GivenAPartialPicker_WhenLocking_ThenTheyAreIncomplete()
    {
        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA), Game(GameB)],
            [Member(Alyson, [GameA], submittedUtc: null)]));

        result.Members.Should().ContainSingle()
            .Which.Should().Be(new LockedMemberStatus(Alyson, SubmissionStatus.Incomplete));
    }

    [Fact]
    public void GivenAMemberWhoNeverStarted_WhenLocking_ThenTheyStillGetAnIncompleteRow()
    {
        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA), Game(GameB)],
            [Member(Dance, [], submittedUtc: null)]));

        result.Members.Should().ContainSingle()
            .Which.Should().Be(new LockedMemberStatus(Dance, SubmissionStatus.Incomplete));
    }

    /// <remarks>
    /// Section 4's "a game added after Submit reverts the member to In Progress" carries straight
    /// through to lock: they never re-submitted, so they lock as Incomplete.
    /// </remarks>
    [Fact]
    public void GivenAGameAddedAfterSubmit_WhenLocking_ThenTheMemberIsIncomplete()
    {
        WeekLockGame late = Game(GameC) with { AddedUtc = LockAt.AddHours(-1) };

        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA), late],
            [Member(Michael, [GameA, GameC], submittedUtc: LockAt.AddHours(-3))]));

        result.Members.Should().ContainSingle()
            .Which.Status.Should().Be(SubmissionStatus.Incomplete);
    }

    [Fact]
    public void GivenAMemberWhoJoinedAfterTheLockInstant_WhenLocking_ThenTheyGetNoRow()
    {
        WeekLockMember lateJoiner = Member(Dance, [], submittedUtc: null) with
        {
            JoinedUtc = LockAt.AddMinutes(1),
        };

        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA)],
            [Member(Michael, [GameA], LockAt.AddHours(-1)), lateJoiner]));

        result.Members.Select(member => member.MembershipId).Should().Equal(Michael);
    }

    [Fact]
    public void GivenAMemberWhoJoinedExactlyAtTheLockInstant_WhenLocking_ThenTheyGetARow()
    {
        WeekLockMember onTheBell = Member(Dance, [], submittedUtc: null) with { JoinedUtc = LockAt };

        WeekLockResult result = WeekLocker.Lock(Request([Game(GameA)], [onTheBell]));

        result.Members.Should().ContainSingle()
            .Which.Should().Be(new LockedMemberStatus(Dance, SubmissionStatus.Incomplete));
    }

    [Fact]
    public void GivenAMemberRemovedBeforeLock_WhenLocking_ThenTheyGetNoRow()
    {
        WeekLockMember removed = Member(Alyson, [GameA], LockAt.AddHours(-1)) with { IsActive = false };

        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA)],
            [removed, Member(Michael, [GameA], LockAt.AddHours(-1))]));

        result.Members.Select(member => member.MembershipId).Should().Equal(Michael);
    }

    [Fact]
    public void GivenARemovedGame_WhenLocking_ThenItIsNeitherSnapshottedNorCounted()
    {
        WeekLockGame removed = Game(GameB) with { IsActive = false };

        WeekLockResult result = WeekLocker.Lock(Request(
            [Game(GameA), removed],
            [Member(Michael, [GameA], LockAt.AddHours(-1))]));

        result.Games.Select(game => game.GameSetGameId).Should().Equal(GameA);

        // Every game that still counts is picked, and Submit was pressed after the last add.
        result.Members.Should().ContainSingle().Which.Status.Should().Be(SubmissionStatus.Locked);
    }

    [Fact]
    public void GivenAVoidedGame_WhenLocking_ThenItIsExcludedTheSameWay()
    {
        WeekLockGame voided = Game(GameB) with { IsActive = false };

        WeekLockResult result = WeekLocker.Lock(Request([Game(GameA), voided], []));

        result.Games.Select(game => game.GameSetGameId).Should().Equal(GameA);
    }

    [Fact]
    public void GivenAGameWithALine_WhenLocking_ThenTheSpreadIsSnapshotted()
    {
        WeekLockGame game = Game(GameA) with { CurrentSpread = -6.5m };

        WeekLockResult result = WeekLocker.Lock(Request([game], []));

        result.Games.Should().ContainSingle()
            .Which.Should().Be(new LockedGameSnapshot(GameA, -6.5m, DefaultPointValue));
    }

    [Fact]
    public void GivenAGameWithNoLine_WhenLocking_ThenTheSnapshottedSpreadIsNull()
    {
        WeekLockResult result = WeekLocker.Lock(Request([Game(GameA)], []));

        result.Games.Should().ContainSingle().Which.SpreadAtLock.Should().BeNull();
    }

    [Fact]
    public void GivenACloseSpreadRule_WhenLocking_ThenTheFrozenValueIsResolvedAgainstTheSnapshottedSpread()
    {
        PointRuleInfo closeSpread = new(
            Priority: 1,
            RuleType: PointRuleType.CloseSpread,
            ConferenceId: null,
            TeamId: null,
            SpreadThreshold: 3m,
            PointValue: 9,
            RuleId: null);

        WeekLockRequest request = Request([Game(GameA) with { CurrentSpread = -2.5m }], []) with
        {
            PointRules = [closeSpread],
        };

        WeekLockResult result = WeekLocker.Lock(request);

        result.Games.Should().ContainSingle()
            .Which.Should().Be(new LockedGameSnapshot(GameA, -2.5m, 9));
    }

    [Fact]
    public void GivenAnOverride_WhenLocking_ThenItWinsOverEveryRule()
    {
        PointRuleInfo conferenceRule = new(
            Priority: 1,
            RuleType: PointRuleType.ConferenceGame,
            ConferenceId: null,
            TeamId: null,
            SpreadThreshold: null,
            PointValue: 7,
            RuleId: null);

        WeekLockRequest request = Request([Game(GameA) with { PointValueOverride = 42 }], []) with
        {
            PointRules = [conferenceRule],
        };

        WeekLocker.Lock(request).Games.Should().ContainSingle().Which.FrozenPointValue.Should().Be(42);
    }

    [Fact]
    public void GivenNothingMatches_WhenLocking_ThenTheFrozenValueIsTheLeagueDefault()
    {
        WeekLocker.Lock(Request([Game(GameA)], []))
            .Games.Should().ContainSingle().Which.FrozenPointValue.Should().Be(DefaultPointValue);
    }

    [Fact]
    public void GivenAnEmptySet_WhenLocking_ThenEveryMemberIsIncomplete()
    {
        WeekLockResult result = WeekLocker.Lock(Request(
            [],
            [Member(Michael, [], submittedUtc: LockAt.AddHours(-1))]));

        result.Games.Should().BeEmpty();
        result.Members.Should().ContainSingle().Which.Status.Should().Be(SubmissionStatus.Incomplete);
    }

    [Fact]
    public void GivenNoRequest_WhenLocking_ThenItThrows()
    {
        Action act = () => WeekLocker.Lock(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static WeekLockRequest Request(
        IReadOnlyList<WeekLockGame> games,
        IReadOnlyList<WeekLockMember> members) => new()
        {
            Games = games,
            Members = members,
            LeagueDefaultPointValue = DefaultPointValue,
            LockInstantUtc = LockAt,
        };

    private static WeekLockGame Game(Guid gameSetGameId) => new(
        gameSetGameId,
        SetBuilt,
        IsActive: true,
        new PointGameInfo(HomeTeam, AwayTeam, Conference, Conference, IsConferenceGame: true),
        PointValueOverride: null,
        CurrentSpread: null);

    private static WeekLockMember Member(Guid membershipId, Guid[] picked, DateTime? submittedUtc) =>
        new(membershipId, SetBuilt.AddDays(-30), IsActive: true, picked, submittedUtc);
}
