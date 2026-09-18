using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Leagues;

/// <summary>Pure invariant checks (P1-01): <see cref="LeagueRules"/>.</summary>
public sealed class LeagueRulesTests
{
    private static readonly IReadOnlyList<SeasonWeek> Weeks =
    [
        new SeasonWeek(2026, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(6), IsRegularSeason: true),
        new SeasonWeek(2026, 1, DateTimeOffset.UnixEpoch.AddDays(7), DateTimeOffset.UnixEpoch.AddDays(13), IsRegularSeason: true),
        new SeasonWeek(2026, 14, DateTimeOffset.UnixEpoch.AddDays(98), DateTimeOffset.UnixEpoch.AddDays(104), IsRegularSeason: true),
        new SeasonWeek(2026, 15, DateTimeOffset.UnixEpoch.AddDays(105), DateTimeOffset.UnixEpoch.AddDays(111), IsRegularSeason: false),
    ];

    [Theory]
    [InlineData("  My League  ", "My League")]
    [InlineData("X", "X")]
    public void GivenAValidName_WhenValidating_ThenItIsTrimmed(string input, string expected) =>
        LeagueRules.ValidateName(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GivenAnEmptyName_WhenValidating_ThenItThrows(string? input)
    {
        Action act = () => LeagueRules.ValidateName(input);
        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidName);
    }

    [Fact]
    public void GivenAFiftyOneCharacterName_WhenValidating_ThenItThrows()
    {
        string tooLong = new('A', League.NameMaxLength + 1);

        Action act = () => LeagueRules.ValidateName(tooLong);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidName);
    }

    [Fact]
    public void GivenAFiftyCharacterName_WhenValidating_ThenItIsAccepted()
    {
        string exactlyFifty = new('A', League.NameMaxLength);

        LeagueRules.ValidateName(exactlyFifty).Should().Be(exactlyFifty);
    }

    [Fact]
    public void GivenRegularSeasonWeeksInOrder_WhenValidatingWeekRange_ThenItPasses()
    {
        Action act = () => LeagueRules.ValidateWeekRange(1, 14, Weeks);
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenFirstAfterLast_WhenValidatingWeekRange_ThenItThrows()
    {
        Action act = () => LeagueRules.ValidateWeekRange(14, 1, Weeks);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidWeekRange);
    }

    [Fact]
    public void GivenAChampionshipWeek_WhenValidatingWeekRange_ThenItThrows()
    {
        Action act = () => LeagueRules.ValidateWeekRange(1, 15, Weeks);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidWeekRange);
    }

    [Fact]
    public void GivenACapAtFifty_WhenEnsuringRoom_ThenItThrows()
    {
        Action act = () => LeagueRules.EnsureRoomForNewMember(LeagueRules.MemberCap);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.LeagueFull);
    }

    [Fact]
    public void GivenRoomUnderTheCap_WhenEnsuringRoom_ThenItPasses()
    {
        Action act = () => LeagueRules.EnsureRoomForNewMember(LeagueRules.MemberCap - 1);
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenTheSameMembership_WhenEnsuringNotActingOnSelf_ThenItThrows()
    {
        Guid id = Guid.CreateVersion7();

        Action act = () => LeagueRules.EnsureNotActingOnSelf(id, id, "remove");

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.CannotActOnSelf);
    }

    [Fact]
    public void GivenDifferentMemberships_WhenEnsuringNotActingOnSelf_ThenItPasses()
    {
        Action act = () => LeagueRules.EnsureNotActingOnSelf(Guid.CreateVersion7(), Guid.CreateVersion7(), "remove");
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenZeroRemainingCommissioners_WhenEnsuringAtLeastOneRemains_ThenItThrows()
    {
        Action act = () => LeagueRules.EnsureAtLeastOneCommissionerRemains(0);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.LastCommissioner);
    }

    [Fact]
    public void GivenOneRemainingCommissioner_WhenEnsuringAtLeastOneRemains_ThenItPasses()
    {
        Action act = () => LeagueRules.EnsureAtLeastOneCommissionerRemains(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenAValidTransfer_WhenApplying_ThenTargetIsPromotedAndCallerIsDemoted()
    {
        Guid leagueId = Guid.CreateVersion7();
        var caller = new Membership { Id = Guid.CreateVersion7(), LeagueId = leagueId, Role = MembershipRole.Commissioner };
        var target = new Membership { Id = Guid.CreateVersion7(), LeagueId = leagueId, Role = MembershipRole.Member };

        LeagueRules.ApplyTransfer(caller, target);

        target.Role.Should().Be(MembershipRole.Commissioner);
        caller.Role.Should().Be(MembershipRole.Member);
    }

    [Fact]
    public void GivenAThirdCommissioner_WhenTransferring_ThenTheyAreUntouched()
    {
        Guid leagueId = Guid.CreateVersion7();
        var caller = new Membership { Id = Guid.CreateVersion7(), LeagueId = leagueId, Role = MembershipRole.Commissioner };
        var target = new Membership { Id = Guid.CreateVersion7(), LeagueId = leagueId, Role = MembershipRole.Member };
        var other = new Membership { Id = Guid.CreateVersion7(), LeagueId = leagueId, Role = MembershipRole.Commissioner };

        LeagueRules.ApplyTransfer(caller, target);

        // ApplyTransfer never touches anything but caller and target; a third commissioner's
        // role object is simply never passed in.
        other.Role.Should().Be(MembershipRole.Commissioner);
    }

    [Fact]
    public void GivenTransferToSelf_WhenApplying_ThenItThrows()
    {
        var caller = new Membership { Id = Guid.CreateVersion7(), LeagueId = Guid.CreateVersion7(), Role = MembershipRole.Commissioner };

        Action act = () => LeagueRules.ApplyTransfer(caller, caller);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidTransferTarget);
    }

    [Fact]
    public void GivenAnInactiveTarget_WhenTransferring_ThenItThrows()
    {
        Guid leagueId = Guid.CreateVersion7();
        var caller = new Membership { Id = Guid.CreateVersion7(), LeagueId = leagueId, Role = MembershipRole.Commissioner };
        var target = new Membership
        {
            Id = Guid.CreateVersion7(),
            LeagueId = leagueId,
            Role = MembershipRole.Member,
            RemovedUtc = DateTime.UtcNow,
        };

        Action act = () => LeagueRules.ApplyTransfer(caller, target);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidTransferTarget);
    }

    [Fact]
    public void GivenATargetInAnotherLeague_WhenTransferring_ThenItThrows()
    {
        var caller = new Membership { Id = Guid.CreateVersion7(), LeagueId = Guid.CreateVersion7(), Role = MembershipRole.Commissioner };
        var target = new Membership { Id = Guid.CreateVersion7(), LeagueId = Guid.CreateVersion7(), Role = MembershipRole.Member };

        Action act = () => LeagueRules.ApplyTransfer(caller, target);

        act.Should().Throw<LeagueRuleViolation>()
            .Which.Code.Should().Be(LeagueRuleViolationCode.InvalidTransferTarget);
    }

    [Fact]
    public void GivenNullOrEmptyDisplayName_WhenValidatingOverride_ThenItClears()
    {
        LeagueRules.ValidateDisplayNameOverride(null).Should().BeNull();
        LeagueRules.ValidateDisplayNameOverride("   ").Should().BeNull();
    }

    [Fact]
    public void GivenATooLongDisplayName_WhenValidatingOverride_ThenItThrows()
    {
        string tooLong = new('A', Membership.DisplayNameMaxLength + 1);

        Action act = () => LeagueRules.ValidateDisplayNameOverride(tooLong);

        act.Should().Throw<LeagueRuleViolation>();
    }

    [Fact]
    public void GivenATrimmableValidDisplayName_WhenValidatingOverride_ThenItIsTrimmed() =>
        LeagueRules.ValidateDisplayNameOverride("  Nickname  ").Should().Be("Nickname");
}
