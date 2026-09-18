using NcaafPickEm.Domain.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Points;

/// <summary>
/// Point value resolution (Feature 03, <c>04-Domain-Algorithms.md</c> section 3).
/// </summary>
public sealed class PointValueResolverTests
{
    private const int LeagueDefault = 10;

    private static readonly Guid HomeTeam = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AwayTeam = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTeam = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Sec = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BigTen = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void GivenNoRules_WhenResolving_ThenTheGameIsWorthTheLeagueDefault()
    {
        var resolution = Resolve(NonConferenceGame(), rules: []);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenRulesThatAllMiss_WhenResolving_ThenTheGameIsWorthTheLeagueDefault()
    {
        PointRuleInfo[] rules =
        [
            ConferenceRule(priority: 0, conferenceId: null, points: 20),
            TeamRule(priority: 1, teamId: OtherTeam, points: 30),
        ];

        var resolution = Resolve(NonConferenceGame(), rules);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenExactlyOneMatchingRule_WhenResolving_ThenTheGameIsWorthThatRulesValue()
    {
        var ruleId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        PointRuleInfo[] rules = [TeamRule(priority: 0, teamId: HomeTeam, points: 25, ruleId: ruleId)];

        var resolution = Resolve(NonConferenceGame(), rules);

        resolution.Should().Be(new PointResolution(25, PointValueSource.Rule, ruleId));
    }

    [Fact]
    public void GivenSeveralMatchingRules_WhenResolving_ThenTheLowestPriorityNumberWins()
    {
        var topRuleId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        PointRuleInfo[] rules =
        [
            TeamRule(priority: 0, teamId: HomeTeam, points: 25, ruleId: topRuleId),
            ConferenceRule(priority: 1, conferenceId: null, points: 40),
        ];

        var resolution = Resolve(ConferenceGame(), rules);

        resolution.Should().Be(new PointResolution(25, PointValueSource.Rule, topRuleId));
    }

    [Fact]
    public void GivenMatchingRulesPassedOutOfOrder_WhenResolving_ThenTheyAreSortedByPriorityFirst()
    {
        var topRuleId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        PointRuleInfo[] unsorted =
        [
            ConferenceRule(priority: 7, conferenceId: null, points: 40),
            TeamRule(priority: 2, teamId: HomeTeam, points: 25, ruleId: topRuleId),
        ];

        var resolution = Resolve(ConferenceGame(), unsorted);

        resolution.Should().Be(new PointResolution(25, PointValueSource.Rule, topRuleId));
    }

    [Theory]
    [InlineData(-2.5, true)]
    [InlineData(2.5, true)]
    [InlineData(0.0, true)]
    [InlineData(-3.0, false)]
    [InlineData(3.0, false)]
    [InlineData(-10.5, false)]
    public void GivenACloseSpreadRule_WhenTheGameHasASpread_ThenOnlySpreadsInsideTheThresholdMatch(
        double spread,
        bool expectMatch)
    {
        PointRuleInfo[] rules = [CloseSpreadRule(priority: 0, threshold: 3m, points: 20)];

        var resolution = Resolve(NonConferenceGame(), rules, currentSpread: (decimal)spread);

        resolution.Value.Should().Be(expectMatch ? 20 : LeagueDefault);
        resolution.Source.Should().Be(expectMatch ? PointValueSource.Rule : PointValueSource.Default);
    }

    [Fact]
    public void GivenACloseSpreadRule_WhenTheGameHasNoSpread_ThenTheRuleDoesNotMatch()
    {
        PointRuleInfo[] rules = [CloseSpreadRule(priority: 0, threshold: 3m, points: 20)];

        var resolution = Resolve(NonConferenceGame(), rules, currentSpread: null);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenACloseSpreadRuleWithNoThreshold_WhenResolving_ThenTheRuleDoesNotMatch()
    {
        // Validation rejects this rule; the resolver must not treat the missing threshold as zero,
        // infinity, or a crash.
        PointRuleInfo[] rules =
        [
            new PointRuleInfo(0, PointRuleType.CloseSpread, null, null, null, 20),
        ];

        var resolution = Resolve(NonConferenceGame(), rules, currentSpread: -1m);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenAManualOverride_WhenRulesAndTheDefaultAlsoApply_ThenTheOverrideWins()
    {
        PointRuleInfo[] rules =
        [
            TeamRule(priority: 0, teamId: HomeTeam, points: 25),
            ConferenceRule(priority: 1, conferenceId: null, points: 40),
        ];

        var resolution = Resolve(ConferenceGame(), rules, pointValueOverride: 7);

        resolution.Should().Be(new PointResolution(7, PointValueSource.Override, null));
    }

    [Fact]
    public void GivenAConferenceRuleWithNoConference_WhenTheGameIsAConferenceGame_ThenItMatches()
    {
        PointRuleInfo[] rules = [ConferenceRule(priority: 0, conferenceId: null, points: 20)];

        var resolution = Resolve(ConferenceGame(), rules);

        resolution.Value.Should().Be(20);
        resolution.Source.Should().Be(PointValueSource.Rule);
    }

    [Fact]
    public void GivenAConferenceRuleWithNoConference_WhenTheGameIsNotAConferenceGame_ThenItDoesNotMatch()
    {
        PointRuleInfo[] rules = [ConferenceRule(priority: 0, conferenceId: null, points: 20)];

        var resolution = Resolve(NonConferenceGame(), rules);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenAConferenceRuleForOneConference_WhenBothTeamsAreInIt_ThenItMatches()
    {
        PointRuleInfo[] rules = [ConferenceRule(priority: 0, conferenceId: Sec, points: 20)];

        var resolution = Resolve(ConferenceGame(), rules);

        resolution.Value.Should().Be(20);
        resolution.Source.Should().Be(PointValueSource.Rule);
    }

    [Fact]
    public void GivenAConferenceRuleForOneConference_WhenItIsAnotherConferencesGame_ThenItDoesNotMatch()
    {
        PointRuleInfo[] rules = [ConferenceRule(priority: 0, conferenceId: BigTen, points: 20)];

        var resolution = Resolve(ConferenceGame(), rules);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenAConferenceRuleForOneConference_WhenOnlyOneTeamIsInIt_ThenItDoesNotMatch()
    {
        // Flagged as a conference game but the two teams disagree: half a match is no match.
        var mismatched = new PointGameInfo(HomeTeam, AwayTeam, Sec, BigTen, IsConferenceGame: true);
        PointRuleInfo[] rules = [ConferenceRule(priority: 0, conferenceId: Sec, points: 20)];

        var resolution = Resolve(mismatched, rules);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenAConferenceRuleForOneConference_WhenTheGameIsNotFlaggedAsAConferenceGame_ThenItDoesNotMatch()
    {
        // Both teams in the conference, but the provider says it is not a conference game.
        var crossOver = new PointGameInfo(HomeTeam, AwayTeam, Sec, Sec, IsConferenceGame: false);
        PointRuleInfo[] rules = [ConferenceRule(priority: 0, conferenceId: Sec, points: 20)];

        var resolution = Resolve(crossOver, rules);

        resolution.Should().Be(new PointResolution(LeagueDefault, PointValueSource.Default, null));
    }

    [Fact]
    public void GivenATeamRule_WhenTheTeamIsAtHome_ThenItMatches()
    {
        PointRuleInfo[] rules = [TeamRule(priority: 0, teamId: HomeTeam, points: 30)];

        Resolve(NonConferenceGame(), rules).Value.Should().Be(30);
    }

    [Fact]
    public void GivenATeamRule_WhenTheTeamIsAway_ThenItMatches()
    {
        PointRuleInfo[] rules = [TeamRule(priority: 0, teamId: AwayTeam, points: 30)];

        Resolve(NonConferenceGame(), rules).Value.Should().Be(30);
    }

    [Fact]
    public void GivenATeamRule_WhenTheTeamIsNotPlaying_ThenItDoesNotMatch()
    {
        PointRuleInfo[] rules = [TeamRule(priority: 0, teamId: OtherTeam, points: 30)];

        Resolve(NonConferenceGame(), rules).Value.Should().Be(LeagueDefault);
    }

    [Fact]
    public void GivenACandidateRuleThatWasNeverSaved_WhenItMatches_ThenNoMatchedRuleIdIsReported()
    {
        PointRuleInfo[] rules = [TeamRule(priority: 0, teamId: HomeTeam, points: 30, ruleId: null)];

        var resolution = Resolve(NonConferenceGame(), rules);

        resolution.Should().Be(new PointResolution(30, PointValueSource.Rule, null));
    }

    [Theory]
    [InlineData(20, 10, true)]
    [InlineData(11, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(5, 10, false)]
    public void GivenAResolvedValue_WhenComparingToTheDefault_ThenOnlyMoreThanTheDefaultIsElevated(
        int resolved,
        int leagueDefault,
        bool expected)
    {
        PointValueResolver.IsElevated(resolved, leagueDefault).Should().Be(expected);
    }

    [Fact]
    public void GivenNoRuleList_WhenResolving_ThenItThrows()
    {
        Action resolve = () => PointValueResolver.Resolve(NonConferenceGame(), null, LeagueDefault, null!, null);

        resolve.Should().Throw<ArgumentNullException>();
    }

    private static PointResolution Resolve(
        PointGameInfo game,
        IReadOnlyList<PointRuleInfo> rules,
        int? pointValueOverride = null,
        decimal? currentSpread = null) =>
        PointValueResolver.Resolve(game, pointValueOverride, LeagueDefault, rules, currentSpread);

    private static PointGameInfo ConferenceGame() =>
        new(HomeTeam, AwayTeam, Sec, Sec, IsConferenceGame: true);

    private static PointGameInfo NonConferenceGame() =>
        new(HomeTeam, AwayTeam, Sec, BigTen, IsConferenceGame: false);

    private static PointRuleInfo ConferenceRule(int priority, Guid? conferenceId, int points, Guid? ruleId = null) =>
        new(priority, PointRuleType.ConferenceGame, conferenceId, null, null, points, ruleId);

    private static PointRuleInfo CloseSpreadRule(int priority, decimal threshold, int points, Guid? ruleId = null) =>
        new(priority, PointRuleType.CloseSpread, null, null, threshold, points, ruleId);

    private static PointRuleInfo TeamRule(int priority, Guid teamId, int points, Guid? ruleId = null) =>
        new(priority, PointRuleType.Team, null, teamId, null, points, ruleId);
}
