using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="FixtureSeeder"/> (P2-05): reference data and the demo league load once and stay
/// idempotent on every later call.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class FixtureSeederTests
{
    private readonly ApiTestFixture _fixture;

    public FixtureSeederTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenReferenceDataAlreadySeeded_WhenSeedingAgain_ThenNoDuplicateRowsAreWritten()
    {
        using WebApplicationFactory<Program> app = _fixture.Factory.WithWebHostBuilder(_ => { });

        await RunSeederTwiceAsync(app, seedDemoLeague: false);

        await using AsyncServiceScope scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        int teamCount = await database.Teams.CountAsync();

        // Scoped to the fixture's own season/week: P3-03's overflow tests (409 over the 50-game
        // cap) insert their own synthetic games under unrelated season years so they can exceed
        // 50 eligible games without touching this count.
        int gameCount = await database.Games.CountAsync(g => g.SeasonYear == 2026 && g.Week == 7);

        teamCount.Should().Be(40); // The 12 real captured teams + 28 invented; see teams.json.
        gameCount.Should().Be(16);
    }

    [Fact]
    public async Task GivenDemoLeagueSeeding_WhenSeededTwice_ThenExactlyOneLeagueWithFiveMembersExists()
    {
        using WebApplicationFactory<Program> app = _fixture.Factory.WithWebHostBuilder(_ => { });

        await RunSeederTwiceAsync(app, seedDemoLeague: true);

        await using AsyncServiceScope scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        League[] leagues = await database.Leagues
            .Where(l => l.Name == FixtureSeeder.DemoLeagueName)
            .ToArrayAsync();
        leagues.Should().ContainSingle();

        League league = leagues[0];
        Membership[] memberships = await database.Memberships
            .Where(m => m.LeagueId == league.Id)
            .Include(m => m.User)
            .ToArrayAsync();

        memberships.Should().HaveCount(5);
        memberships.Select(m => m.User!.DisplayName).Should()
            .BeEquivalentTo(FixtureSeeder.DemoMembers);
        memberships.Single(m => m.User!.DisplayName == "Michael").Role.Should().Be(MembershipRole.Commissioner);
        memberships.Where(m => m.User!.DisplayName != "Michael").Should()
            .OnlyContain(m => m.Role == MembershipRole.Member);
    }

    private static async Task RunSeederTwiceAsync(WebApplicationFactory<Program> app, bool seedDemoLeague)
    {
        for (int i = 0; i < 2; i++)
        {
            await using AsyncServiceScope scope =
                app.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            FixtureSeeder seeder = scope.ServiceProvider.GetRequiredService<FixtureSeeder>();
            await seeder.SeedAsync(seedDemoLeague);
        }
    }
}
