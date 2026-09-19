using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The P2-04 admin actions: <c>POST /api/admin/refresh/{dataType}</c>,
/// <c>POST /api/admin/unmatched/{id}/resolve</c>, and the counter/needs-review additions to
/// <c>GET /api/admin/data-status</c>.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AdminEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public AdminEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnonymousCaller_WhenRefreshing_ThenItIs401()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync("/api/admin/refresh/Teams", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenANonCommissioner_WhenRefreshing_ThenItIs403()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.PostAsync("/api/admin/refresh/Teams", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GivenACommissioner_WhenRefreshingTeams_ThenItIs202AndWritesAManualRefreshAuditRow()
    {
        // A real refresh writes DataRefreshStatus(Teams), which would break
        // AdminDataStatusTests's "every slice is null until something runs" assumption on the
        // shared fixture database; this test gets its own throwaway one instead
        // (ProviderCallRecorderTests does the same for the same reason).
        await using SqlTestDatabase database = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(database.ConnectionString);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);
        using HttpClient client = factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsync("/api/admin/refresh/Teams", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        ManualRefreshResponse? body = await response.Content.ReadFromJsonAsync<ManualRefreshResponse>();
        body.Should().NotBeNull();
        body!.DataType.Should().Be(RefreshDataType.Teams);
        body.Success.Should().BeTrue();

        bool auditWritten = await factory.QueryDbAsync(db => db.AuditLog
            .AsNoTracking()
            .Where(entry => entry.LeagueId == scenario.LeagueId && entry.Action == AuditAction.ManualRefresh)
            .AnyAsync());
        auditWritten.Should().BeTrue();
    }

    [Fact]
    public async Task GivenADatabaseWithNoLeagues_WhenAnyUserBootstrapsTheReferenceData_ThenItIsAllowed()
    {
        // P8-06, D-166: nobody commissions anything until the first league exists, and creating
        // that league needs the season calendar, so the two bootstrap routes admit any signed-in
        // caller while the Leagues table is empty. Its own database: the shared one has leagues.
        await using SqlTestDatabase database = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(database.ConnectionString);

        Domain.Users.User user = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db));
        using HttpClient client = factory.CreateMutatingClientAs(user.Id);

        using HttpResponseMessage status = await client.GetAsync("/api/admin/data-status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage refresh = await client.PostAsync("/api/admin/refresh/Teams", null);
        refresh.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // There is no league for the ManualRefresh audit row to belong to, and that must not be
        // an error (D-079's stand-in league does not exist yet).
        (await factory.QueryDbAsync(db => db.AuditLog.AnyAsync())).Should().BeFalse();
    }

    [Fact]
    public async Task GivenALeagueExistsAndTheCallerCommissionsNothing_WhenBootstrappingTheReferenceData_ThenItIs403()
    {
        await using SqlTestDatabase database = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(database.ConnectionString);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);
        using HttpClient client = factory.CreateMutatingClientAs(scenario.MemberUserId);

        using HttpResponseMessage status = await client.GetAsync("/api/admin/data-status");
        status.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpResponseMessage refresh = await client.PostAsync("/api/admin/refresh/Teams", null);
        refresh.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GivenAnUnknownDataType_WhenRefreshing_ThenItIs400()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsync("/api/admin/refresh/NotAThing", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnmatchedGameAndAResolvingGame_WhenResolved_ThenAliasesAreCreatedAndItIsMarkedResolved()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        (Guid unmatchedId, Guid gameId, Guid homeTeamId, Guid awayTeamId) = await _fixture.Factory.QueryDbAsync(async database =>
        {
            Game game = await database.Games
                .Where(g => g.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear && g.Week == 7)
                .FirstAsync();

            var unmatched = new UnmatchedGame
            {
                Id = Guid.CreateVersion7(),
                Source = ProviderSource.Espn,
                RawHomeName = $"Raw Home {Guid.CreateVersion7().ToString()[..8]}",
                RawAwayName = $"Raw Away {Guid.CreateVersion7().ToString()[..8]}",
                GameDate = DateOnly.FromDateTime(game.KickoffUtc),
                RawPayload = "{}",
                FirstSeenUtc = DateTime.UtcNow,
            };
            database.UnmatchedGames.Add(unmatched);
            await database.SaveChangesAsync();

            return (unmatched.Id, game.Id, game.HomeTeamId, game.AwayTeamId);
        });

        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/admin/unmatched/{unmatchedId}/resolve", new ResolveUnmatchedRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await _fixture.Factory.ExecuteDbAsync(async database =>
        {
            UnmatchedGame resolved = await database.UnmatchedGames.SingleAsync(row => row.Id == unmatchedId);
            resolved.ResolvedUtc.Should().NotBeNull();

            bool homeAlias = await database.TeamAliases
                .AnyAsync(alias => alias.TeamId == homeTeamId && alias.Source == ProviderSource.Espn);
            bool awayAlias = await database.TeamAliases
                .AnyAsync(alias => alias.TeamId == awayTeamId && alias.Source == ProviderSource.Espn);
            homeAlias.Should().BeTrue();
            awayAlias.Should().BeTrue();
        });
    }

    [Fact]
    public async Task GivenAnEspnPayloadCarryingAnEventId_WhenResolved_ThenTheGameLearnsIt()
    {
        // The payload is written by LiveScoreApplyService with web (camel-cased) JSON defaults;
        // reading it back with the case-sensitive defaults silently learns nothing.
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(testDatabase.ConnectionString);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);

        (Guid unmatchedId, Guid gameId) = await factory.QueryDbAsync(async database =>
        {
            Game game = await database.Games
                .Where(g => g.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear && g.Week == 7)
                .FirstAsync();
            game.EspnEventId = null;

            var unmatched = new UnmatchedGame
            {
                Id = Guid.CreateVersion7(),
                Source = ProviderSource.Espn,
                RawHomeName = $"Payload Home {Guid.CreateVersion7().ToString()[..8]}",
                RawAwayName = $"Payload Away {Guid.CreateVersion7().ToString()[..8]}",
                GameDate = DateOnly.FromDateTime(game.KickoffUtc),
                RawPayload = """{"sourceEventId":"401628500","homeName":"H","awayName":"A"}""",
                FirstSeenUtc = DateTime.UtcNow,
            };
            database.UnmatchedGames.Add(unmatched);
            await database.SaveChangesAsync();

            return (unmatched.Id, game.Id);
        });

        using HttpClient client = factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/admin/unmatched/{unmatchedId}/resolve", new ResolveUnmatchedRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        long? learned = await factory.QueryDbAsync(database => database.Games
            .AsNoTracking()
            .Where(g => g.Id == gameId)
            .Select(g => g.EspnEventId)
            .SingleAsync());
        learned.Should().Be(401628500);
    }

    [Fact]
    public async Task GivenARawNameAnotherTeamAlreadyOwns_WhenResolved_ThenItIs409AndNothingIsWritten()
    {
        // IX_TeamAliases_Source_Alias is unique across teams, so this must be reported, not left
        // to surface as a unique-index violation (D-089).
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(testDatabase.ConnectionString);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);
        string claimedAlias = $"Claimed {Guid.CreateVersion7().ToString()[..8]}";

        (Guid unmatchedId, Guid gameId) = await factory.QueryDbAsync(async database =>
        {
            Game game = await database.Games
                .Where(g => g.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear && g.Week == 7)
                .FirstAsync();

            Guid otherTeamId = await database.Teams
                .Where(team => team.Id != game.HomeTeamId && team.Id != game.AwayTeamId)
                .Select(team => team.Id)
                .FirstAsync();

            database.TeamAliases.Add(new TeamAlias
            {
                Id = Guid.CreateVersion7(),
                TeamId = otherTeamId,
                Source = ProviderSource.Espn,
                Alias = claimedAlias,
            });

            var unmatched = new UnmatchedGame
            {
                Id = Guid.CreateVersion7(),
                Source = ProviderSource.Espn,
                RawHomeName = claimedAlias,
                RawAwayName = $"Free {Guid.CreateVersion7().ToString()[..8]}",
                GameDate = DateOnly.FromDateTime(game.KickoffUtc),
                RawPayload = "{}",
                FirstSeenUtc = DateTime.UtcNow,
            };
            database.UnmatchedGames.Add(unmatched);
            await database.SaveChangesAsync();

            return (unmatched.Id, game.Id);
        });

        using HttpClient client = factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/admin/unmatched/{unmatchedId}/resolve", new ResolveUnmatchedRequest(gameId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        DateTime? resolvedUtc = await factory.QueryDbAsync(database => database.UnmatchedGames
            .AsNoTracking()
            .Where(row => row.Id == unmatchedId)
            .Select(row => row.ResolvedUtc)
            .SingleAsync());
        resolvedUtc.Should().BeNull("a refused resolve must leave the row for a human to fix");
    }

    [Fact]
    public async Task GivenAnEmptyGameId_WhenResolved_ThenItIs400FromValidation()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/admin/unmatched/{Guid.NewGuid()}/resolve", new ResolveUnmatchedRequest(Guid.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnknownGameId_WhenResolved_ThenItIs400()
    {
        // Its own database: the unresolved row this seeds would break AdminDataStatusTests's
        // "nothing is unmatched yet" assertion on the shared fixture database.
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(testDatabase.ConnectionString);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);

        Guid unmatchedId = await factory.QueryDbAsync(async database =>
        {
            var unmatched = new UnmatchedGame
            {
                Id = Guid.CreateVersion7(),
                Source = ProviderSource.Espn,
                RawHomeName = $"Nowhere Home {Guid.CreateVersion7().ToString()[..8]}",
                RawAwayName = $"Nowhere Away {Guid.CreateVersion7().ToString()[..8]}",
                GameDate = new DateOnly(2026, 10, 17),
                RawPayload = "{}",
                FirstSeenUtc = DateTime.UtcNow,
            };
            database.UnmatchedGames.Add(unmatched);
            await database.SaveChangesAsync();
            return unmatched.Id;
        });

        using HttpClient client = factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/admin/unmatched/{unmatchedId}/resolve", new ResolveUnmatchedRequest(Guid.CreateVersion7()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnknownUnmatchedId_WhenResolved_ThenItIs404()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateMutatingClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/admin/unmatched/{Guid.NewGuid()}/resolve", new ResolveUnmatchedRequest(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(799, false)]
    [InlineData(800, true)]
    public async Task GivenNCfbdCallsThisMonth_WhenReadingDataStatus_ThenTheWarningMatchesTheThreshold(
        int callCount, bool expectedWarning)
    {
        await using SqlTestDatabase database = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(database.ConnectionString);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);

        await factory.ExecuteDbAsync(async db =>
        {
            for (int i = 0; i < callCount; i++)
            {
                db.ProviderCalls.Add(new ProviderCall
                {
                    Id = Guid.CreateVersion7(),
                    Provider = nameof(ProviderSource.Cfbd),
                    Operation = "GetGames",
                    StartedUtc = DateTime.UtcNow,
                    DurationMs = 10,
                    Success = true,
                });
            }

            await db.SaveChangesAsync();
        });

        using HttpClient client = factory.CreateClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await client.GetAsync("/api/admin/data-status");

        DataStatusResponse? body = await response.Content.ReadFromJsonAsync<DataStatusResponse>();
        body.Should().NotBeNull();
        body!.CfbdCallsThisMonth.Should().Be(callCount);
        body.CfbdWarning.Should().Be(expectedWarning);
    }

    [Fact]
    public async Task GivenAFinalTieGameInAnActiveLeagueSet_WhenReadingDataStatus_ThenItListsAsNeedsReview()
    {
        // Mutates a fixture game's Status/scores directly, which would leak into any other test
        // reading the shared fixture's Games table; this test gets its own throwaway database.
        await using SqlTestDatabase testDatabase = await SqlTestDatabase.CreateAsync();
        await using var factory = new ApiFactory(testDatabase.ConnectionString);

        (Guid leagueId, Guid tieGameId) = await SeedTieGameSetAsync(factory);

        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(factory);
        using HttpClient client = factory.CreateClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.GetAsync("/api/admin/data-status");
        DataStatusResponse? body = await response.Content.ReadFromJsonAsync<DataStatusResponse>();

        body.Should().NotBeNull();
        body!.NeedsReview.Should().Contain(row => row.GameId == tieGameId && row.LeagueId == leagueId);
    }

    /// <summary>
    /// Marks a fixture Week 7 game Final with equal scores (a tie, "needs review") inside its own
    /// league set, without going through the live-score poller — this is a data-status-shape
    /// test, not a poller test.
    /// </summary>
    private static async Task<(Guid LeagueId, Guid GameId)> SeedTieGameSetAsync(ApiFactory factory)
    {
        return await factory.QueryDbAsync(async database =>
        {
            Game game = await database.Games
                .Where(g => g.SeasonYear == FixtureSeasonWeekSource.FixtureSeasonYear && g.Week == 7)
                .FirstAsync();

            game.Status = GameStatus.Final;
            game.HomeScore = 24;
            game.AwayScore = 24;

            Domain.Users.User owner = await TestUsers.CreateUserAsync(database);
            var league = new League
            {
                Id = Guid.CreateVersion7(),
                Name = $"Needs Review League {Guid.CreateVersion7().ToString()[..8]}",
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
                LockAtUtc = game.KickoffUtc,
            };
            var setGame = new WeekGameSetGame
            {
                Id = Guid.CreateVersion7(),
                WeekGameSetId = set.Id,
                GameId = game.Id,
                Source = GameSetGameSource.Rule,
                ResolvedPointValue = 10,
            };

            database.Leagues.Add(league);
            database.WeekGameSets.Add(set);
            database.WeekGameSetGames.Add(setGame);
            await database.SaveChangesAsync();

            return (league.Id, game.Id);
        });
    }
}
