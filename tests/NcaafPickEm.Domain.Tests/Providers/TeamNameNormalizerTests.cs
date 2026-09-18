using NcaafPickEm.Infrastructure.Providers;

namespace NcaafPickEm.Domain.Tests.Providers;

/// <summary>
/// <see cref="TeamNameNormalizer"/> (P2-03): the exact folding rules the matcher depends on.
/// </summary>
public sealed class TeamNameNormalizerTests
{
    [Theory]
    [InlineData("Texas", "texas")]
    [InlineData("  Ohio   State ", "ohio state")]
    [InlineData("San José State", "san jose state")]
    [InlineData("Hawai'i", "hawaii")]
    [InlineData("Hawaii", "hawaii")]
    [InlineData("Texas A&M", "texas am")]
    [InlineData("Miami (OH)", "miami oh")]
    [InlineData("Ohio University", "ohio")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void GivenAProviderSpelling_WhenNormalized_ThenItBecomesTheExpectedKey(string? input, string expected) =>
        TeamNameNormalizer.Normalize(input).Should().Be(expected);

    [Fact]
    public void GivenMiamiAndMiamiOhio_WhenNormalized_ThenTheyStayDistinct() =>
        TeamNameNormalizer.Normalize("Miami (OH)").Should().NotBe(TeamNameNormalizer.Normalize("Miami"));
}
