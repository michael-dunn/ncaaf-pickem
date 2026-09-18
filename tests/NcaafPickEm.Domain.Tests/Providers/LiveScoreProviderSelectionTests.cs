using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NcaafPickEm.Infrastructure;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;

namespace NcaafPickEm.Domain.Tests.Providers;

/// <summary>
/// The <c>Providers:LiveScores</c> switch (P2-03): Fixture serves the sample week directly, Espn
/// and Cfbd both go through the composite so the fallback can engage at runtime, and anything
/// else is a startup failure rather than a silent default.
/// </summary>
public sealed class LiveScoreProviderSelectionTests
{
    [Fact]
    public void GivenFixtureIsConfigured_WhenResolved_ThenTheFixtureProviderAnswersDirectly()
    {
        using ServiceProvider services = Build("Fixture");

        Resolve(services).Should().BeOfType<FixtureLiveScoreProvider>();
        services.GetRequiredService<ILiveScoreHealth>().ConfiguredSource.Should().Be(LiveScoreSource.Fixture);
    }

    [Theory]
    [InlineData("Espn", LiveScoreSource.Espn)]
    [InlineData("Cfbd", LiveScoreSource.Cfbd)]
    public void GivenARealSourceIsConfigured_WhenResolved_ThenTheCompositeAnswers(
        string configured,
        LiveScoreSource expected)
    {
        using ServiceProvider services = Build(configured);

        Resolve(services).Should().BeOfType<CompositeLiveScoreProvider>();

        ILiveScoreHealth health = services.GetRequiredService<ILiveScoreHealth>();
        health.ConfiguredSource.Should().Be(expected);
        health.ActiveSource.Should().Be(expected);
        health.ScoresMayBeStale.Should().BeFalse();
    }

    [Fact]
    public void GivenAnUnknownSource_WhenRegistering_ThenStartupFails()
    {
        FluentActions.Invoking(() => Build("Sportradar"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Providers:LiveScores*");
    }

    [Fact]
    public void GivenNoSourceOutsideDevelopment_WhenRegistering_ThenStartupFails()
    {
        FluentActions.Invoking(() => Build(liveScores: null, environmentName: "Production"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Providers:LiveScores is not set*");
    }

    private static ILiveScoreProvider Resolve(ServiceProvider services) =>
        services.CreateScope().ServiceProvider.GetRequiredService<ILiveScoreProvider>();

    private static ServiceProvider Build(string? liveScores, string environmentName = "Testing")
    {
        Dictionary<string, string?> settings = new()
        {
            ["Providers:ReferenceData"] = "Fixture",
            ["Providers:LiveScores"] = liveScores,
            ["Jobs:Enabled"] = "false",
        };

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration, new TestEnvironment(environmentName))
            .BuildServiceProvider();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "NcaafPickEm.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
