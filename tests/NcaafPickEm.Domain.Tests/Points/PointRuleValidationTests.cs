using NcaafPickEm.Domain.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Points;

/// <summary>
/// Point configuration validation (Feature 03): values 1..100, threshold greater than 0,
/// priorities unique.
/// </summary>
public sealed class PointRuleValidationTests
{
    private static readonly Guid Sec = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Team = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void GivenALegalConfiguration_WhenValidating_ThenThereAreNoErrors()
    {
        PointRuleInfo[] rules =
        [
            new(0, PointRuleType.ConferenceGame, Sec, null, null, 20),
            new(1, PointRuleType.CloseSpread, null, null, 3m, 15),
            new(2, PointRuleType.Team, null, Team, null, 12),
        ];

        PointRuleValidation.Validate(rules, leagueDefault: 10).Should().BeEmpty();
    }

    [Fact]
    public void GivenNoRulesAtAll_WhenValidating_ThenOnlyTheDefaultIsChecked()
    {
        PointRuleValidation.Validate([], leagueDefault: 10).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void GivenALeagueDefaultOutsideOneToOneHundred_WhenValidating_ThenItIsRejected(int leagueDefault)
    {
        var errors = PointRuleValidation.Validate([], leagueDefault);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be(PointRuleValidation.DefaultPointValueField);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void GivenARulePointValueOutsideOneToOneHundred_WhenValidating_ThenItIsRejected(int pointValue)
    {
        PointRuleInfo[] rules = [new(0, PointRuleType.ConferenceGame, null, null, null, pointValue)];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[0].PointValue");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void GivenAPointValueAtTheEdgeOfTheRange_WhenValidating_ThenItIsAccepted(int pointValue)
    {
        PointRuleInfo[] rules = [new(0, PointRuleType.ConferenceGame, null, null, null, pointValue)];

        PointRuleValidation.Validate(rules, pointValue).Should().BeEmpty();
    }

    [Fact]
    public void GivenACloseSpreadRuleWithNoThreshold_WhenValidating_ThenItIsRejected()
    {
        PointRuleInfo[] rules = [new(0, PointRuleType.CloseSpread, null, null, null, 20)];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[0].SpreadThreshold");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.5)]
    public void GivenACloseSpreadThresholdThatIsNotPositive_WhenValidating_ThenItIsRejected(double threshold)
    {
        PointRuleInfo[] rules = [new(0, PointRuleType.CloseSpread, null, null, (decimal)threshold, 20)];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[0].SpreadThreshold");
    }

    [Fact]
    public void GivenAConferenceRuleWithNoConference_WhenValidating_ThenItIsAccepted()
    {
        // Null means "any conference game", which is a legal rule.
        PointRuleInfo[] rules = [new(0, PointRuleType.ConferenceGame, null, null, null, 20)];

        PointRuleValidation.Validate(rules, leagueDefault: 10).Should().BeEmpty();
    }

    [Fact]
    public void GivenATeamRuleWithNoTeam_WhenValidating_ThenItIsRejected()
    {
        PointRuleInfo[] rules = [new(0, PointRuleType.Team, null, null, null, 20)];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[0].TeamId");
    }

    [Fact]
    public void GivenATeamRuleWithAnEmptyTeamId_WhenValidating_ThenItIsRejected()
    {
        PointRuleInfo[] rules = [new(0, PointRuleType.Team, null, Guid.Empty, null, 20)];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[0].TeamId");
    }

    [Fact]
    public void GivenTwoRulesWithTheSamePriority_WhenValidating_ThenTheSecondIsRejected()
    {
        PointRuleInfo[] rules =
        [
            new(0, PointRuleType.ConferenceGame, null, null, null, 20),
            new(0, PointRuleType.Team, null, Team, null, 30),
        ];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[1].Priority");
    }

    [Fact]
    public void GivenAnUnknownRuleType_WhenValidating_ThenItIsRejected()
    {
        PointRuleInfo[] rules = [new(0, (PointRuleType)99, null, null, null, 20)];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 10);

        errors.Should().ContainSingle()
            .Which.Field.Should().Be("Rules[0].RuleType");
    }

    [Fact]
    public void GivenSeveralProblems_WhenValidating_ThenEveryOneIsReported()
    {
        PointRuleInfo[] rules =
        [
            new(0, PointRuleType.CloseSpread, null, null, null, 0),
            new(0, PointRuleType.Team, null, null, null, 30),
        ];

        var errors = PointRuleValidation.Validate(rules, leagueDefault: 0);

        errors.Select(error => error.Field).Should().Equal(
            PointRuleValidation.DefaultPointValueField,
            "Rules[0].PointValue",
            "Rules[0].SpreadThreshold",
            "Rules[1].Priority",
            "Rules[1].TeamId");
    }

    [Fact]
    public void GivenNoRuleList_WhenValidating_ThenItThrows()
    {
        Action validate = () => PointRuleValidation.Validate(null!, leagueDefault: 10);

        validate.Should().Throw<ArgumentNullException>();
    }
}
