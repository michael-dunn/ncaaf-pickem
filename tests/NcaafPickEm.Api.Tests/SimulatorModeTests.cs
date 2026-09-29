using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Cfbd;
using NcaafPickEm.Infrastructure.Providers.Simulator;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P11-01 (D-183): <c>Providers:*=Simulator</c> hands the database to tools/NcaafPickEm.Simulator,
/// so the app reads the calendar from the database, ingests and polls nothing, and refuses the
/// mode anywhere but Development and Testing. The simulator's own tests boot the app in this mode
/// over a database the simulator wrote; these pin the wiring and the refusal.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class SimulatorModeTests
{
    private readonly ApiTestFixture _fixture;

    public SimulatorModeTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Simulator", "Fixture")]
    [InlineData("Fixture", "Simulator")]
    public void GivenProduction_WhenAProviderIsTheSimulator_ThenTheAppRefusesToStart(string referenceData, string liveScores)
    {
        using var app = new ModeFactory(_fixture.Database.ConnectionString, "Production", referenceData, liveScores);

        Action start = () => _ = app.Services;

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*only allowed in Development and Testing*");
    }

    [Fact]
    public async Task GivenTesting_WhenBothProvidersAreTheSimulator_ThenTheCalendarComesFromTheDatabaseAndNothingIngestsOrPolls()
    {
        await using var app = new ModeFactory(_fixture.Database.ConnectionString, "Testing", "Simulator", "Simulator");

        app.Services.GetServices<IHostedService>().Should().NotContain(service => service is SaturdayPoller);
        app.Services.GetRequiredService<ILiveScoreHealth>().ActiveSource.Should().Be(LiveScoreSource.Simulator);

        using IServiceScope scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISeasonWeekSource>().Should().BeOfType<DbSeasonWeekSource>();
        scope.ServiceProvider.GetRequiredService<IReferenceDataProvider>().Should().BeOfType<SimulatorOwnedReferenceDataProvider>();
        scope.ServiceProvider.GetRequiredService<ILiveScoreProvider>().Should().BeOfType<SimulatorOwnedLiveScoreProvider>();
        scope.ServiceProvider.GetServices<IScheduledJob>().Select(job => job.Name)
            .Should().NotContain(name => name.Contains("Refresh", StringComparison.Ordinal))
            .And.Contain("RegenerateGameSets", "the app's own jobs still run against the simulated week");

        Func<Task> refresh = () => scope.ServiceProvider.GetRequiredService<IReferenceDataProvider>().GetGamesAsync(2026, 1);
        await refresh.Should().ThrowAsync<InvalidOperationException>().WithMessage("*owned by NcaafPickEm.Simulator*");
    }

    private sealed class ModeFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _environment;
        private readonly string _referenceData;
        private readonly string _liveScores;

        public ModeFactory(string connectionString, string environment, string referenceData, string liveScores)
        {
            _connectionString = connectionString;
            _environment = environment;
            _referenceData = referenceData;
            _liveScores = liveScores;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jobs:Enabled", "false");
            builder.UseSetting("Providers:ReferenceData", _referenceData);
            builder.UseSetting("Providers:LiveScores", _liveScores);
            builder.UseSetting("Seed:DemoLeague", "false");
            builder.UseSetting(DatabaseDefaults.MigrateOnStartupKey, "false");
        }
    }
}
