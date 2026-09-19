using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Scoring;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>POST .../gameset/games/{gameId}/void</c> (Feature 06, P5-02): a commissioner voiding a
/// game outright so it scores for nobody, always after lock.
/// </summary>
/// <remarks>
/// Its own throwaway database, exactly like <see cref="OverrideTests"/> and
/// <see cref="ScoringServiceTests"/>.
/// </remarks>
public sealed class VoidTests : IAsyncLifetime
{
    private readonly RecordingStandingsSnapshotWriter _snapshotWriter = new();
    private SqlTestDatabase _database = null!;
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _database = await SqlTestDatabase.CreateAsync();
        _factory = new ApiFactory(
            _database.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IStandingsSnapshotWriter>();
                services.AddSingleton<IStandingsSnapshotWriter>(_snapshotWriter);
            });

        await FixtureGameData.EnsureSeededAsync(_factory);

        _factory.Services.GetRequiredService<FixtureSnapshotState>().Set(FixtureSnapshotState.MaxSnapshot);

        await using AsyncServiceScope scope = _factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await SaturdayPoller.PollOnceAsync(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            ScoredWeekScenario.FixtureSaturday,
            scope.ServiceProvider.GetRequiredService<ILogger<SaturdayPoller>>(),
            CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenAGameIsVoided_ThenItAwardsNothingAndDropsOutOfActiveGameCount()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        // Establish a baseline: rescore once (via overriding nothing - void the tie so the
        // baseline itself is a complete, known week) before voiding the late game.
        using HttpResponseMessage tieVoid = await VoidAsync(scenario, scenario.TieGameSetGameId, "Baseline.");
        tieVoid.StatusCode.Should().Be(HttpStatusCode.OK);

        int pointsBefore = await PointsAsync(scenario, scenario.HomePickerMembershipId);
        int activeBefore = await ActiveGameCountAsync(scenario, scenario.HomePickerMembershipId);

        using HttpResponseMessage response = await VoidAsync(scenario, scenario.LateGameSetGameId, "Weather.");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        int pointsAfter = await PointsAsync(scenario, scenario.HomePickerMembershipId);
        pointsAfter.Should().Be(pointsBefore - ScoredWeekScenario.LateGamePointValue);

        int activeAfter = await ActiveGameCountAsync(scenario, scenario.HomePickerMembershipId);
        activeAfter.Should().Be(activeBefore - 1);
    }

    [Fact]
    public async Task GivenTheOnlyPendingGameIsVoided_ThenTheWeekCompletes()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage response = await VoidAsync(scenario, scenario.TieGameSetGameId, "Weather.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IsCompleteAsync(scenario)).Should().BeTrue();
    }

    [Fact]
    public async Task GivenAVoidedGame_ThenTheGridShowsVoided()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage response = await VoidAsync(scenario, scenario.LateGameSetGameId, "Weather.");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        GameSetGameDto? dto = await response.Content.ReadFromJsonAsync<GameSetGameDto>();
        dto!.IsVoided.Should().BeTrue();

        bool rowVoided = await _factory.QueryDbAsync(database => database.WeekGameSetGames
            .AsNoTracking()
            .Where(row => row.Id == scenario.LateGameSetGameId)
            .Select(row => row.IsVoided)
            .SingleAsync());
        rowVoided.Should().BeTrue();

        using HttpClient commishClient = _factory.CreateClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage getResponse = await commishClient.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{ScoredWeekScenario.Week}/gameset");
        WeekGameSetResponse? weekResponse = await getResponse.Content.ReadFromJsonAsync<WeekGameSetResponse>();
        weekResponse!.Games.Should().ContainSingle(g => g.GameSetGameId == scenario.LateGameSetGameId && g.IsVoided);
    }

    [Fact]
    public async Task GivenTheWeekHasNotLocked_WhenVoiding_ThenItIs409()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await _factory.ExecuteDbAsync(async database =>
        {
            WeekGameSet set = await database.WeekGameSets.SingleAsync(s => s.Id == scenario.WeekGameSetId);
            set.LockedUtc = null;
            await database.SaveChangesAsync();
        });

        using HttpResponseMessage response = await VoidAsync(scenario, scenario.LateGameSetGameId, "Too early.");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAnAlreadyVoidedGame_WhenVoidingAgain_ThenItIs409()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage first = await VoidAsync(scenario, scenario.LateGameSetGameId, "Weather.");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage second = await VoidAsync(scenario, scenario.LateGameSetGameId, "Weather again.");
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAVoid_WhenAMemberReadsTheAudit_ThenTheEntryIsVisibleWithASensibleSummary()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage voidResponse = await VoidAsync(scenario, scenario.LateGameSetGameId, "Field flooded.");
        voidResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid partialUserId = await UserIdForMembershipAsync(scenario.PartialMembershipId);
        using HttpClient memberClient = _factory.CreateClientAs(partialUserId);

        using HttpResponseMessage auditResponse = await memberClient.GetAsync($"/api/leagues/{scenario.LeagueId}/audit");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        AuditEntry[]? entries = await auditResponse.Content.ReadFromJsonAsync<AuditEntry[]>();
        entries.Should().ContainSingle(entry =>
            entry.Action == nameof(AuditAction.GameVoided)
            && entry.Summary.Contains("Field flooded.", StringComparison.Ordinal));
    }

    private Task<ScoredWeekScenario> CreateScenarioAsync() => ScoredWeekScenario.CreateAsync(_factory);

    private async Task<HttpResponseMessage> VoidAsync(ScoredWeekScenario scenario, Guid gameSetGameId, string reason)
    {
        Guid gameId = await _factory.QueryDbAsync(database => database.WeekGameSetGames
            .Where(row => row.Id == gameSetGameId)
            .Select(row => row.GameId)
            .SingleAsync());

        using HttpClient client = _factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        return await client.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{ScoredWeekScenario.Week}/gameset/games/{gameId}/void",
            new VoidGameRequest(reason));
    }

    private Task<Guid> UserIdForMembershipAsync(Guid membershipId) =>
        _factory.QueryDbAsync(database => database.Memberships
            .Where(membership => membership.Id == membershipId)
            .Select(membership => membership.UserId)
            .SingleAsync());

    private Task<int> PointsAsync(ScoredWeekScenario scenario, Guid membershipId) =>
        _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId && row.MembershipId == membershipId)
            .Select(row => row.Points)
            .SingleAsync());

    private Task<int> ActiveGameCountAsync(ScoredWeekScenario scenario, Guid membershipId) =>
        _factory.QueryDbAsync(database => database.WeekResults
            .AsNoTracking()
            .Where(row => row.WeekGameSetId == scenario.WeekGameSetId && row.MembershipId == membershipId)
            .Select(row => row.ActiveGameCount)
            .SingleAsync());

    private Task<bool> IsCompleteAsync(ScoredWeekScenario scenario) =>
        _factory.QueryDbAsync(database => database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.IsComplete)
            .SingleAsync());
}
