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
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>POST .../gameset/games/{gameId}/override-result</c> (Feature 06, P5-02): a commissioner
/// correcting a game's winner by hand, always after lock.
/// </summary>
/// <remarks>
/// Its own throwaway database, like <see cref="ScoringServiceTests"/>: <see cref="InitializeAsync"/>
/// writes the fixture's final scores onto the shared <c>Games</c> rows once (including the Iowa
/// State / Kansas tie every override test corrects), and every test then seeds its own league
/// over them.
/// </remarks>
public sealed class OverrideTests : IAsyncLifetime
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

        // Every game final, including the Iowa State / Kansas tie (24-24) and the post-midnight
        // finish - the same starting point ScoringServiceTests uses.
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
    public async Task GivenALockedFinalTie_WhenOverridden_ThenTheWinnerIsSetAndTheWeekRescoresAndCompletes()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage response = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "Kansas won on the field, ESPN reported a bad final.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GameSetGameDto? dto = await response.Content.ReadFromJsonAsync<GameSetGameDto>();
        dto!.WinnerTeamId.Should().Be(scenario.TieAwayTeamId);
        dto.IsVoided.Should().BeFalse();

        // Rescoring happened via ResultOverridden, not a direct call: the away picker (Kansas)
        // now earns the tie's points, and the week - whose only unresolved game was this tie -
        // is complete.
        int awayPickerPoints = await PointsAsync(scenario, scenario.AwayPickerMembershipId);
        awayPickerPoints.Should().BeGreaterThanOrEqualTo(ScoredWeekScenario.TiePointValue);

        (await IsCompleteAsync(scenario)).Should().BeTrue();
    }

    [Fact]
    public async Task GivenTheWeekHasNotLocked_WhenOverriding_ThenItIs409()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();
        await _factory.ExecuteDbAsync(async database =>
        {
            WeekGameSet set = await database.WeekGameSets.SingleAsync(s => s.Id == scenario.WeekGameSetId);
            set.LockedUtc = null;
            await database.SaveChangesAsync();
        });

        using HttpResponseMessage response = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "Too early.");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GivenAWinnerThatIsNotInTheGame_WhenOverriding_ThenItIs400()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage response = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, Guid.NewGuid(), "Wrong team entirely.");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAVoidedRow_WhenOverriding_ThenItIs409()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage voidResponse = await VoidAsync(scenario, scenario.TieGameSetGameId, "Weather.");
        voidResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage response = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "Too late, it's voided.");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <remarks>
    /// Decision (documented in AGENT-NOTES.md / DECISIONS.md): a second, identical override is
    /// not a silent no-op. It writes a fresh audit row and re-raises the event, so the audit
    /// trail records the commissioner confirming the call a second time.
    /// </remarks>
    [Fact]
    public async Task GivenTheSameOverrideCalledTwice_WhenOverriding_ThenBothCallsSucceedAndBothAreAudited()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage first = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "First call.");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage second = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "Confirmed again.");
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        int auditCount = await _factory.QueryDbAsync(database => database.AuditLog
            .AsNoTracking()
            .CountAsync(entry => entry.LeagueId == scenario.LeagueId && entry.Action == AuditAction.ResultOverride));

        auditCount.Should().Be(2);
    }

    [Fact]
    public async Task GivenAnOverride_WhenAMemberReadsTheAudit_ThenTheEntryIsVisibleWithASensibleSummary()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        using HttpResponseMessage overrideResponse = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "Official box score corrected.");
        overrideResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid awayPickerUserId = await UserIdForMembershipAsync(scenario.AwayPickerMembershipId);
        using HttpClient memberClient = _factory.CreateClientAs(awayPickerUserId);

        using HttpResponseMessage auditResponse = await memberClient.GetAsync($"/api/leagues/{scenario.LeagueId}/audit");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        AuditEntry[]? entries = await auditResponse.Content.ReadFromJsonAsync<AuditEntry[]>();
        entries.Should().ContainSingle(entry =>
            entry.Action == nameof(AuditAction.ResultOverride)
            && entry.Summary.Contains("Kansas", StringComparison.Ordinal)
            && entry.Summary.Contains("Official box score corrected.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAnOverriddenTie_WhenReadingDataStatus_ThenItNoLongerListsAsNeedsReview()
    {
        ScoredWeekScenario scenario = await CreateScenarioAsync();

        Guid tieGameId = await _factory.QueryDbAsync(database => database.WeekGameSetGames
            .Where(row => row.Id == scenario.TieGameSetGameId)
            .Select(row => row.GameId)
            .SingleAsync());

        using HttpResponseMessage overrideResponse = await OverrideAsync(
            scenario, scenario.TieGameSetGameId, scenario.TieAwayTeamId, "Resolved.");
        overrideResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpClient commishClient = _factory.CreateClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage statusResponse = await commishClient.GetAsync("/api/admin/data-status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        DataStatusResponse? body = await statusResponse.Content.ReadFromJsonAsync<DataStatusResponse>();
        body!.NeedsReview.Should().NotContain(row => row.GameId == tieGameId && row.LeagueId == scenario.LeagueId);
    }

    private Task<ScoredWeekScenario> CreateScenarioAsync() => ScoredWeekScenario.CreateAsync(_factory);

    private async Task<HttpResponseMessage> OverrideAsync(
        ScoredWeekScenario scenario, Guid gameSetGameId, Guid winnerTeamId, string reason)
    {
        Guid gameId = await _factory.QueryDbAsync(database => database.WeekGameSetGames
            .Where(row => row.Id == gameSetGameId)
            .Select(row => row.GameId)
            .SingleAsync());

        using HttpClient client = _factory.CreateMutatingClientAs(scenario.CommissionerUserId);
        return await client.PostAsJsonAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{ScoredWeekScenario.Week}/gameset/games/{gameId}/override-result",
            new OverrideResultRequest(winnerTeamId, reason));
    }

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

    private Task<bool> IsCompleteAsync(ScoredWeekScenario scenario) =>
        _factory.QueryDbAsync(database => database.WeekGameSets
            .AsNoTracking()
            .Where(set => set.Id == scenario.WeekGameSetId)
            .Select(set => set.IsComplete)
            .SingleAsync());
}
