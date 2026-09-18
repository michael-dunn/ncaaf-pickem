using NcaafPickEm.Fixtures;

namespace NcaafPickEm.Domain.Tests;

/// <summary>
/// Placeholder from P0-01 so <c>dotnet test</c> is green on a clean clone.
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
    public void GivenP2_05Fixtures_WhenListingFixtures_ThenLoaderReportsTheWeek7SampleWeek()
    {
        // P2-05 filled tests/NcaafPickEm.Fixtures/Data with the Week 7, 2026 sample week and the
        // Feature 05 worked example; see FixtureLoaderTests for the exact shape assertions.
        FixtureLoader.Names.Should().NotBeEmpty();
        FixtureLoader.Names.Should().Contain("Week7_2026/schedule.json");
        FixtureLoader.Names.Should().Contain("influence-example.json");
    }

    [Fact]
    public void GivenUnknownFixture_WhenReading_ThenItThrowsFileNotFound()
    {
        Action read = () => FixtureLoader.ReadText("Nope/missing.json");

        read.Should().Throw<FileNotFoundException>();
    }
}
