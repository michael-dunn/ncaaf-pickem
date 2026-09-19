using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Seasons.Events;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The Phase 2 exit criterion: with the fixture Week 7, 2026 schedule seeded and a league set
/// containing its games, advancing <see cref="FixtureSnapshotState"/> from 1 through 6 and
/// invoking one <see cref="SaturdayPoller.PollOnceAsync"/> per snapshot applies scores and raises
/// <see cref="GameWentFinal"/> for every game exactly once.
/// </summary>
public sealed class SaturdayPollerIntegrationTests
{
    /// <summary>Every fixture Week 7 kickoff lands on this Eastern calendar date (AGENT-NOTES "Fixtures").</summary>
    private static readonly DateOnly FixtureSaturday = new(2026, 10, 17);

    [Fact]
    public async Task GivenTheFixtureWeekSeededInALeagueSet_WhenAdvancingEverySnapshotAndPolling_ThenEveryGameGoesFinalExactlyOnce()
    {
        // Its own throwaway database and app: advancing FixtureSnapshotState and writing
        // DataRefreshStatus(Scores) here must not leak into the shared ApiTestFixture database
        // other test classes assert against (AdminDataStatusTests in particular).
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(testDatabase.ConnectionString);

        List<Guid> gameIds = await SeedWeekGameSetAsync(factory);
        gameIds.Should().NotBeEmpty("the fixture must have seeded Week 7, 2026 games by the time the app has started");

        FixtureSnapshotState snapshotState = factory.Services.GetRequiredService<FixtureSnapshotState>();
        List<GameWentFinal> allFinalEvents = [];

        for (int snapshot = FixtureSnapshotState.MinSnapshot; snapshot <= FixtureSnapshotState.MaxSnapshot; snapshot++)
        {
            snapshotState.Set(snapshot);

            await using AsyncServiceScope scope = factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            TimeProvider timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            ILogger<SaturdayPoller> logger = scope.ServiceProvider.GetRequiredService<ILogger<SaturdayPoller>>();

            LiveScoreApplyResult result = await SaturdayPoller.PollOnceAsync(
                scope.ServiceProvider, database, timeProvider, FixtureSaturday, logger, CancellationToken.None);

            allFinalEvents.AddRange(result.Events.OfType<GameWentFinal>());
        }

        // Re-applying the last snapshot must change nothing and raise nothing: that is what makes
        // the five-minute cadence, the catch-up window and a manual refresh all harmless
        // (04-Domain-Algorithms.md section 9).
        await using (AsyncServiceScope scope = factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            LiveScoreApplyResult replay = await SaturdayPoller.PollOnceAsync(
                scope.ServiceProvider,
                database,
                scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                FixtureSaturday,
                scope.ServiceProvider.GetRequiredService<ILogger<SaturdayPoller>>(),
                CancellationToken.None);

            replay.Changed.Should().Be(0, "snapshot 6 was already applied");
            replay.Events.Should().BeEmpty("a repeated snapshot raises nothing");
        }

        List<Guid> finalizedGameIds = [.. allFinalEvents.Select(e => e.GameId)];

        foreach (Guid gameId in gameIds)
        {
            finalizedGameIds.Count(id => id == gameId).Should().Be(
                1, $"game {gameId} should go final exactly once across snapshots 1..6");
        }

        await factory.ExecuteDbAsync(async database =>
        {
            List<Game> games = await database.Games.Where(g => gameIds.Contains(g.Id)).ToListAsync();
            games.Should().AllSatisfy(g => g.Status.Should().Be(GameStatus.Final));
        });
    }

    /// <summary>
    /// Seeds a league and a <c>WeekGameSet</c>/<c>WeekGameSetGames</c> for the fixture's Week 7
    /// over every game the fixture seeder already loaded at startup, the way P3-03/P3-04's
    /// <c>GameSetService</c> would once merged.
    /// </summary>
    private static async Task<List<Guid>> SeedWeekGameSetAsync(ApiFactory factory)
    {
        return await factory.QueryDbAsync(async database =>
        {
            // Only the games the fixture snapshots actually carry live scores for: the 13
            // Saturday-Eastern FBS-vs-FBS games (AGENT-NOTES "Fixtures"). The other 3 rows on the
            // schedule (2 FCS-involving, 1 plain Friday game) are excluded from the generator's
            // own eligible pool too (04-Domain-Algorithms.md section 2 step 1) and never reach
            // Final in the snapshot timeline.
            List<Game> games = await database.Games
                .Include(g => g.HomeTeam)
                .Include(g => g.AwayTeam)
                .Where(g => g.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear
                    && g.Week == 7
                    && g.IsSaturdayEastern
                    && g.HomeTeam!.Classification == TeamClassification.Fbs
                    && g.AwayTeam!.Classification == TeamClassification.Fbs)
                .ToListAsync();

            if (games.Count == 0)
            {
                return [];
            }

            Domain.Users.User owner = await TestUsers.CreateUserAsync(database);

            var league = new League
            {
                Id = Guid.CreateVersion7(),
                Name = $"Poller Test League {Guid.CreateVersion7().ToString()[..8]}",
                SeasonYear = FixtureSeasonWeekSource.FixtureSeasonYear,
                FirstWeek = 1,
                LastWeek = 14,
                DefaultPointValue = 10,
                CreatedByUserId = owner.Id,
                CreatedUtc = DateTime.UtcNow,
            };

            var set = new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = 7,
                GeneratedUtc = DateTime.UtcNow,
                LockAtUtc = games.Min(g => g.KickoffUtc),
            };

            database.Leagues.Add(league);
            database.WeekGameSets.Add(set);

            foreach (Game game in games)
            {
                database.WeekGameSetGames.Add(new WeekGameSetGame
                {
                    Id = Guid.CreateVersion7(),
                    WeekGameSetId = set.Id,
                    GameId = game.Id,
                    Source = GameSetGameSource.Rule,
                    ResolvedPointValue = 10,
                });
            }

            await database.SaveChangesAsync();

            return games.Select(g => g.Id).ToList();
        });
    }
}
