using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Services;
using NcaafPickEm.Shared.Contracts.Scoring;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// P8-01's fold-in review of P5-02 (corrections): two defects found by reading the code rather
/// than by a failing test.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class CorrectionEdgeCaseTests
{
    /// <summary>A season nothing else in the suite uses, so these rows pollute no other test.</summary>
    private const int ThrowawaySeason = 2097;

    private readonly ApiTestFixture _fixture;

    public CorrectionEdgeCaseTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// D-193. <c>AuditSummaryBuilder</c> read <c>after</c> and <c>week</c> with
    /// <c>JsonElement.GetProperty</c>, which throws <c>KeyNotFoundException</c> — a type the
    /// surrounding <c>catch (JsonException)</c> does not stop. One row whose <c>Details</c> is
    /// valid JSON of the wrong shape therefore 500'd the whole league's audit list.
    /// </summary>
    [Theory]
    [InlineData(AuditAction.ResultOverride, """{"gameId":"not-a-guid"}""")]
    [InlineData(AuditAction.ResultOverride, "{}")]
    [InlineData(AuditAction.GameManuallyAdded, "{}")]
    [InlineData(AuditAction.GameManuallyRemoved, """{"week":"seven"}""")]
    [InlineData(AuditAction.ManualRefresh, "[]")]
    [InlineData(AuditAction.MemberRemoved, "null")]
    public async Task GivenAnAuditRowWithAnUnexpectedDetailsShape_WhenReadingTheAudit_ThenItStillAnswers(
        AuditAction action,
        string details)
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        Guid actorMembershipId = await _fixture.Factory.QueryDbAsync(async database =>
        {
            Membership membership = database.Memberships.Single(
                candidate => candidate.LeagueId == scenario.LeagueId
                    && candidate.UserId == scenario.CommissionerUserId);

            database.AuditLog.Add(new AuditLogEntry
            {
                Id = Guid.CreateVersion7(),
                LeagueId = scenario.LeagueId,
                ActorMembershipId = membership.Id,
                Action = action,
                TargetId = null,
                Details = details,
                CreatedUtc = DateTime.UtcNow,
            });

            await database.SaveChangesAsync();
            return membership.Id;
        });

        actorMembershipId.Should().NotBeEmpty();

        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.MemberUserId);
        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{scenario.LeagueId}/audit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        AuditEntry[]? entries = await response.Content.ReadFromJsonAsync<AuditEntry[]>();
        entries.Should().ContainSingle();
        entries![0].Summary.Should().NotBeNullOrWhiteSpace("a vaguer sentence, never an exception");
    }

    /// <summary>
    /// D-193. A row removed before lock is no longer part of the week, and
    /// <c>CorrectionService</c> refuses to void or override one (404 <c>GameNotFound</c>), so
    /// listing it as "needs review" offered the commissioner an action that could not succeed.
    /// </summary>
    [Fact]
    public async Task GivenALockedWeekWithARemovedPostponedGame_WhenListingNeedsVoidReview_ThenOnlyTheActiveRowIsListed()
    {
        User owner = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateUserAsync(database));
        League league = await _fixture.Factory.QueryDbAsync(database => TestUsers.CreateLeagueAsync(database, owner));

        (Guid activeRowId, Guid removedRowId) = await SeedLockedWeekWithTwoPostponedGamesAsync(league.Id);

        await using AsyncServiceScope scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        GameSetService games = scope.ServiceProvider.GetRequiredService<GameSetService>();
        NeedsVoidReviewItem[] items = await games.ListNeedsVoidReviewAsync(league.Id, CancellationToken.None);

        items.Select(item => item.GameSetGameId).Should().BeEquivalentTo([activeRowId]);
        items.Select(item => item.GameSetGameId).Should().NotContain(removedRowId);
    }

    private async Task<(Guid ActiveRowId, Guid RemovedRowId)> SeedLockedWeekWithTwoPostponedGamesAsync(Guid leagueId)
    {
        Guid activeRowId = Guid.CreateVersion7();
        Guid removedRowId = Guid.CreateVersion7();

        await _fixture.Factory.ExecuteDbAsync(async database =>
        {
            Team home = NewTeam("Needs Review Home");
            Team away = NewTeam("Needs Review Away");
            database.Teams.AddRange(home, away);

            var setId = Guid.CreateVersion7();
            DateTime kickoff = new DateTime(ThrowawaySeason, 10, 17, 16, 0, 0, DateTimeKind.Utc);

            database.WeekGameSets.Add(new WeekGameSet
            {
                Id = setId,
                LeagueId = leagueId,
                Week = 7,
                UsesOverride = false,
                GeneratedUtc = kickoff.AddDays(-3),
                LockAtUtc = kickoff,
                LockedUtc = kickoff,
            });

            foreach ((Guid rowId, bool isRemoved) in new[] { (activeRowId, false), (removedRowId, true) })
            {
                var game = new Game
                {
                    Id = Guid.CreateVersion7(),
                    CfbdGameId = Random.Shared.NextInt64(9_700_000, 9_799_999),
                    SeasonYear = ThrowawaySeason,
                    Week = 7,
                    HomeTeamId = home.Id,
                    AwayTeamId = away.Id,
                    KickoffUtc = kickoff,
                    KickoffEasternDate = DateOnly.FromDateTime(kickoff),
                    IsSaturdayEastern = true,
                    IsConferenceGame = false,
                    Status = GameStatus.Postponed,
                };

                database.Games.Add(game);

                database.WeekGameSetGames.Add(new WeekGameSetGame
                {
                    Id = rowId,
                    WeekGameSetId = setId,
                    GameId = game.Id,
                    Source = GameSetGameSource.Rule,
                    AddedUtc = kickoff.AddDays(-3),
                    IsRemoved = isRemoved,
                    RemovedReason = isRemoved ? "Removed before lock" : null,
                    ResolvedPointValue = 10,
                });
            }

            await database.SaveChangesAsync();
        });

        return (activeRowId, removedRowId);
    }

    private static Team NewTeam(string school) => new()
    {
        Id = Guid.CreateVersion7(),
        CfbdId = Random.Shared.Next(9_700_000, 9_799_999),
        School = $"{school} {Guid.CreateVersion7().ToString()[..8]}",
        Classification = TeamClassification.Fbs,
    };
}
