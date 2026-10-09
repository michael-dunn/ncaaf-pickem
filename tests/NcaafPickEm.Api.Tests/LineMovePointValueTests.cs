using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Point values follow a moved line until the week locks (P3-06, D-189), driven end to end: the
/// real lines ingest appends the moved line and raises <c>GameLineChanged</c>, and the real
/// dispatcher hands it to <see cref="GameLineChangedPointValueHandler"/>, as after the nightly
/// <c>LinesRefreshJob</c>. Every league here has one close-spread rule: under 3 points is worth 25.
/// </summary>
/// <remarks>
/// Its own database and app, like <see cref="ReferenceIngestTests"/>: an ingest writes
/// <c>DataRefreshStatus</c> rows and moves fixture spreads that the shared database's other
/// classes rely on. Each test moves a different game, so their order does not matter.
/// </remarks>
public sealed class LineMovePointValueTests : IAsyncLifetime
{
    private const int Season = FixtureReferenceDataProvider.FixtureSeason;
    private const int Week = FixtureReferenceDataProvider.FixtureWeek;
    private const decimal CloseSpreadThreshold = 3m;
    private const int CloseSpreadPoints = 25;
    private const int DefaultPoints = 10;

    // After the seeded fixture lines (stamped 2026-10-17 10:00Z), so an ingested line is the
    // newest, and before week 7's first kickoff in any set here (16:00Z), so the week is open.
    private static readonly DateTimeOffset StartUtc = new(2026, 10, 17, 11, 0, 0, TimeSpan.Zero);

    private readonly ScriptedLinesProvider _provider = new(new FixtureReferenceDataProvider());
    private readonly FixedTimeProvider _clock = new(StartUtc);
    private SqlTestDatabase _database = null!;
    private ApiFactory _factory = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _factory = new ApiFactory(
            _database.ConnectionString,
            timeProvider: _clock,
            configureServices: services =>
            {
                services.RemoveAll<IReferenceDataProvider>();
                services.AddSingleton<IReferenceDataProvider>(_provider);
            });
        await FixtureGameData.EnsureSeededAsync(_factory);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenACloseSpreadRule_WhenALineMovesInsideTheThreshold_ThenTheGameTakesTheRuleValue()
    {
        (Guid leagueId, HttpClient client) = await CreateLeagueWithCloseSpreadRuleAsync();
        Guid gameId = await AddGameAsync(client, leagueId, 700003); // spread -4
        (await PointValueAsync(client, leagueId, gameId)).Should().Be(DefaultPoints);

        await MoveLineAsync(700003, -2.5m);

        (await PointValueAsync(client, leagueId, gameId)).Should().Be(CloseSpreadPoints);
    }

    [Fact]
    public async Task GivenACloseSpreadRule_WhenALineMovesOutsideTheThreshold_ThenTheGameFallsBackToTheDefault()
    {
        (Guid leagueId, HttpClient client) = await CreateLeagueWithCloseSpreadRuleAsync();
        Guid gameId = await AddGameAsync(client, leagueId, 700008); // spread 2.5
        (await PointValueAsync(client, leagueId, gameId)).Should().Be(CloseSpreadPoints);

        await MoveLineAsync(700008, 6.5m);

        (await PointValueAsync(client, leagueId, gameId)).Should().Be(DefaultPoints);
    }

    [Fact]
    public async Task GivenAGameWithNoLine_WhenACloseLineAppears_ThenTheGameTakesTheRuleValue()
    {
        (Guid leagueId, HttpClient client) = await CreateLeagueWithCloseSpreadRuleAsync();
        Guid gameId = await AddGameAsync(client, leagueId, 700009); // no line
        (await PointValueAsync(client, leagueId, gameId)).Should().Be(DefaultPoints);

        await MoveLineAsync(700009, 1.5m);

        (await PointValueAsync(client, leagueId, gameId)).Should().Be(CloseSpreadPoints);
    }

    [Fact]
    public async Task GivenAManualOverride_WhenTheLineMoves_ThenTheOverrideStands()
    {
        (Guid leagueId, HttpClient client) = await CreateLeagueWithCloseSpreadRuleAsync();
        Guid gameId = await AddGameAsync(client, leagueId, 700001); // spread -7.5
        using (HttpResponseMessage set = await client.PutAsJsonAsync(PointsRoute(leagueId, gameId), new SetPointOverrideRequest(40)))
        {
            set.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await MoveLineAsync(700001, -1m);

        (await PointValueAsync(client, leagueId, gameId)).Should().Be(40);

        // The line did move: clearing the override resolves against it.
        using (HttpResponseMessage clear = await client.PutAsJsonAsync(PointsRoute(leagueId, gameId), new SetPointOverrideRequest(null)))
        {
            clear.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await PointValueAsync(client, leagueId, gameId)).Should().Be(CloseSpreadPoints);
    }

    [Fact]
    public async Task GivenALockedWeek_WhenTheLineMoves_ThenItsPointValueIsUntouchedButTheOpenWeekReresolves()
    {
        (Guid leagueId, HttpClient client) = await CreateLeagueWithCloseSpreadRuleAsync();
        Guid gameId = await AddGameAsync(client, leagueId, 700006); // spread -9.5
        Guid lockedRowId = await AddLockedWeekRowAsync(leagueId, week: 6, gameId, resolvedPointValue: 99);

        await MoveLineAsync(700006, -1m);

        int lockedValue = await _factory.QueryDbAsync(db =>
            db.WeekGameSetGames.Where(row => row.Id == lockedRowId).Select(row => row.ResolvedPointValue).SingleAsync());
        lockedValue.Should().Be(99, "nothing after lock may change a point value");
        (await PointValueAsync(client, leagueId, gameId)).Should().Be(CloseSpreadPoints);
    }

    [Fact]
    public async Task GivenTwoLeaguesWithTheSameGame_WhenTheLineMoves_ThenBothReresolve()
    {
        (Guid firstLeagueId, HttpClient firstClient) = await CreateLeagueWithCloseSpreadRuleAsync();
        (Guid secondLeagueId, HttpClient secondClient) = await CreateLeagueWithCloseSpreadRuleAsync();
        Guid gameId = await AddGameAsync(firstClient, firstLeagueId, 700011); // spread -6
        await AddGameAsync(secondClient, secondLeagueId, 700011);

        await MoveLineAsync(700011, -2m);

        (await PointValueAsync(firstClient, firstLeagueId, gameId)).Should().Be(CloseSpreadPoints);
        (await PointValueAsync(secondClient, secondLeagueId, gameId)).Should().Be(CloseSpreadPoints);
    }

    private static string PointsRoute(Guid leagueId, Guid gameId) =>
        $"/api/leagues/{leagueId}/weeks/{Week}/gameset/games/{gameId}/points";

    private async Task<(Guid LeagueId, HttpClient Client)> CreateLeagueWithCloseSpreadRuleAsync()
    {
        User commissioner = await _factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "LineCommish"));
        League league = await _factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await _factory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));
        HttpClient client = _factory.CreateMutatingClientAs(commissioner.Id);

        PointRuleDto[] rules = [new(null, 0, PointRuleType.CloseSpread, null, null, CloseSpreadThreshold, CloseSpreadPoints)];
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/leagues/{league.Id}/point-rules", rules);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (league.Id, client);
    }

    private async Task<Guid> AddGameAsync(HttpClient client, Guid leagueId, long cfbdGameId)
    {
        Guid gameId = await FixtureGameData.GetGameIdAsync(_factory, cfbdGameId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/leagues/{leagueId}/weeks/{Week}/gameset/games", new AddGameRequest(gameId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return gameId;
    }

    private async Task<Guid> AddLockedWeekRowAsync(Guid leagueId, int week, Guid gameId, int resolvedPointValue)
    {
        var set = new WeekGameSet
        {
            Id = Guid.CreateVersion7(),
            LeagueId = leagueId,
            Week = week,
            UsesOverride = false,
            GeneratedUtc = StartUtc.UtcDateTime,
            LockedUtc = StartUtc.UtcDateTime,
        };
        var row = new WeekGameSetGame
        {
            Id = Guid.CreateVersion7(),
            WeekGameSetId = set.Id,
            GameId = gameId,
            Source = GameSetGameSource.Manual,
            ResolvedPointValue = resolvedPointValue,
        };

        await _factory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(set);
            db.WeekGameSetGames.Add(row);
            await db.SaveChangesAsync();
        });

        return row.Id;
    }

    private static async Task<int> PointValueAsync(HttpClient client, Guid leagueId, Guid gameId)
    {
        WeekGameSetResponse? body = await client.GetFromJsonAsync<WeekGameSetResponse>($"/api/leagues/{leagueId}/weeks/{Week}/gameset");
        return body!.Games.Single(game => game.GameId == gameId).PointValue;
    }

    /// <summary>
    /// Posts <paramref name="spread"/> as the game's line and runs the real lines ingest a minute
    /// later than the last one, so the new line is the newest.
    /// </summary>
    private async Task MoveLineAsync(long cfbdGameId, decimal spread)
    {
        _provider.Spreads[cfbdGameId] = spread;
        _clock.Set(_clock.GetUtcNow().AddMinutes(1));

        await using AsyncServiceScope scope = _factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        LinesIngestResult result = await scope.ServiceProvider.GetRequiredService<ReferenceDataIngestService>()
            .IngestLinesAsync(Season, Week);
        result.Success.Should().BeTrue();
    }

    /// <summary>The fixture provider, with a test-set spread for any game in <see cref="Spreads"/>.</summary>
    private sealed class ScriptedLinesProvider(IReferenceDataProvider inner) : IReferenceDataProvider
    {
        public Dictionary<long, decimal> Spreads { get; } = [];

        public async Task<IReadOnlyList<ProviderLine>> GetLinesAsync(int season, int week, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ProviderLine> fixture = await inner.GetLinesAsync(season, week, cancellationToken);
            DateTime fetchedUtc = new(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);

            List<ProviderLine> lines = [.. fixture.Where(line => !Spreads.ContainsKey(line.CfbdGameId))];
            lines.AddRange(Spreads.Select(moved => new ProviderLine(moved.Key, "consensus", moved.Value, fetchedUtc)));
            return lines;
        }

        public Task<IReadOnlyList<ProviderConference>> GetConferencesAsync(int season, CancellationToken cancellationToken = default) =>
            inner.GetConferencesAsync(season, cancellationToken);

        public Task<IReadOnlyList<ProviderTeam>> GetTeamsAsync(int season, CancellationToken cancellationToken = default) =>
            inner.GetTeamsAsync(season, cancellationToken);

        public Task<IReadOnlyList<ProviderGame>> GetGamesAsync(int season, int week, CancellationToken cancellationToken = default) =>
            inner.GetGamesAsync(season, week, cancellationToken);

        public Task<IReadOnlyList<ProviderRanking>> GetRankingsAsync(int season, int week, CancellationToken cancellationToken = default) =>
            inner.GetRankingsAsync(season, week, cancellationToken);

        public Task<IReadOnlyList<ProviderCalendarWeek>> GetCalendarAsync(int season, CancellationToken cancellationToken = default) =>
            inner.GetCalendarAsync(season, cancellationToken);
    }
}
