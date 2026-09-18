using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Seeding;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="TeamAliasSeed"/> (P2-03): the verified ESPN spellings from the P2-01 spike load
/// into <c>TeamAliases</c> once, and a second run adds nothing. P2-04's teams refresh calls this.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class TeamAliasSeedTests
{
    private readonly ApiTestFixture _fixture;

    public TeamAliasSeedTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenTheVerifiedAliasDraft_WhenSeededTwice_ThenEveryRowLandsExactlyOnce()
    {
        try
        {
            int firstRun = await SeedAsync();
            int secondRun = await SeedAsync();

            // Ten verified rows, all of whose schools are in the fixture team set. The eleventh
            // (SE Louisiana) is deliberately unverified and is skipped.
            firstRun.Should().Be(10);
            secondRun.Should().Be(0);

            TeamAlias[] aliases = await QueryAsync(db => db.TeamAliases
                .Where(alias => alias.Source == ProviderSource.Espn)
                .ToArrayAsync());

            aliases.Should().HaveCount(10);
            aliases.Select(alias => alias.Alias).Should().Contain("San Jose State").And.Contain("Hawaii");
            aliases.Should().OnlyContain(alias => alias.TeamId != Guid.Empty);

            Team sanJose = await QueryAsync(db => db.Teams.SingleAsync(t => t.School == "San José State"));
            aliases.Where(alias => alias.Alias.StartsWith("San Jos", StringComparison.Ordinal))
                .Should().OnlyContain(alias => alias.TeamId == sanJose.Id);
        }
        finally
        {
            // The API suite shares one database; leave the alias table as it was found.
            await ExecuteAsync(db => db.TeamAliases.ExecuteDeleteAsync());
        }
    }

    private async Task<int> SeedAsync()
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<FixtureSeeder>().SeedAsync(seedDemoLeague: false);

        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("TeamAliasSeedTests");

        return await TeamAliasSeed.SeedAsync(database, logger);
    }

    private async Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task ExecuteAsync(Func<AppDbContext, Task<int>> action) => QueryAsync(action);
}
