using NcaafPickEm.Fixtures;

namespace NcaafPickEm.Domain.Tests;

/// <summary>
/// Placeholder from P0-01 so <c>dotnet test</c> is green on a clean clone.
/// Delete once P0-05 adds <c>SeasonCalendarTests</c>.
/// </summary>
public sealed class ScaffoldTests
{
    [Fact]
    public void GivenCleanClone_WhenDomainTestsRun_ThenTheHarnessWorks()
    {
        const int answer = 42;

        answer.Should().Be(42);
    }

    [Fact]
    public void GivenNoFixturesYet_WhenListingFixtures_ThenLoaderReportsAnEmptySet()
    {
        // P2-05 fills tests/NcaafPickEm.Fixtures/Data; until then the loader must not throw.
        FixtureLoader.Names.Should().BeEmpty();
    }

    [Fact]
    public void GivenUnknownFixture_WhenReading_ThenItThrowsFileNotFound()
    {
        Action read = () => FixtureLoader.ReadText("Nope/missing.json");

        read.Should().Throw<FileNotFoundException>();
    }
}
