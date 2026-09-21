using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;

namespace NcaafPickEm.Domain.Tests.GameSets;

/// <summary>
/// <see cref="SpreadDisplay"/>: the one spelling of a game's line on the picks page (D-181).
/// The stored spread is home-relative (negative = home favored, D-013); the display leads with
/// the favorite and the points it is giving, the way a sportsbook prints it.
/// </summary>
public sealed class SpreadDisplayTests
{
    private static readonly TeamDto Home = new(Guid.NewGuid(), "Michigan", "MICH", null, null);
    private static readonly TeamDto Away = new(Guid.NewGuid(), "Texas", "TEX", null, null);

    [Fact]
    public void GivenNoLine_WhenFormatting_ThenNothingIsShown()
    {
        SpreadDisplay.Format(null, Home, Away).Should().BeNull();
    }

    [Fact]
    public void GivenTheHomeTeamIsFavored_WhenFormatting_ThenItLeadsWithHomeAndTheNegativeLine()
    {
        SpreadDisplay.Format(-7.5m, Home, Away).Should().Be("MICH -7.5");
    }

    [Fact]
    public void GivenTheAwayTeamIsFavored_WhenFormatting_ThenItLeadsWithAwayAndFlipsTheSign()
    {
        SpreadDisplay.Format(3m, Home, Away).Should().Be("TEX -3");
    }

    [Fact]
    public void GivenAWholeNumberLine_WhenFormatting_ThenNoTrailingDecimalIsShown()
    {
        SpreadDisplay.Format(-14.0m, Home, Away).Should().Be("MICH -14");
    }

    [Fact]
    public void GivenAZeroLine_WhenFormatting_ThenItIsAPickEm()
    {
        SpreadDisplay.Format(0m, Home, Away).Should().Be(SpreadDisplay.PickEm);
    }

    [Fact]
    public void GivenAFavoriteWithNoAbbreviation_WhenFormatting_ThenTheSchoolNameIsUsed()
    {
        var unabbreviated = new TeamDto(Guid.NewGuid(), "Sam Houston", null, null, null);

        SpreadDisplay.Format(-2.5m, unabbreviated, Away).Should().Be("Sam Houston -2.5");
    }
}
