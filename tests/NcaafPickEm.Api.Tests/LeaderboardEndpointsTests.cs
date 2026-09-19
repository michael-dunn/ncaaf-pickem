using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Contracts.Picks;
using NcaafPickEm.Shared.Contracts.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The leaderboard endpoints (Feature 07, P5-03): season standings, one week's standings, and the
/// post-lock grid. <c>WeekResults</c> rows are seeded directly - P5-01's scorer is what writes them
/// in production, and this suite is about what the queries make of them.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LeaderboardEndpointsTests
{
    private readonly ApiTestFixture _fixture;

    public LeaderboardEndpointsTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenScoredWeeks_WhenReadingTheSeasonLeaderboard_ThenRanksTotalsBehindAndWinsAreRight()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        Membership joiner = await AddMemberAsync(scenario, "Zoe Joiner", joinedWeek: 7);
        Membership former = await AddMemberAsync(scenario, "Gone Away", joinedWeek: 1, removed: true);

        Guid weekSix = await SeedWeekSetAsync(scenario.LeagueId, week: 6, isComplete: true);
        await SeedResultAsync(weekSix, scenario.MemberMembershipId, points: 30);
        await SeedResultAsync(weekSix, scenario.SecondMemberMembershipId, points: 20);
        await SeedResultAsync(weekSix, former.Id, points: 40);

        await MarkCompleteAsync(scenario.WeekGameSetId);
        await SeedResultAsync(scenario.WeekGameSetId, scenario.MemberMembershipId, points: 10);
        await SeedResultAsync(scenario.WeekGameSetId, scenario.SecondMemberMembershipId, points: 20);
        await SeedResultAsync(scenario.WeekGameSetId, joiner.Id, points: 25);

        // A stray result for a week before the joiner arrived must never reach their total.
        await SeedResultAsync(weekSix, joiner.Id, points: 5);

        SeasonLeaderboard body = await GetSeasonAsync(scenario, scenario.MemberUserId);

        body.ThroughWeek.Should().Be(7, "week 7 is the latest week with any result row");
        body.Rows.Should().HaveCount(4, "the commissioner and three members are active; the former member is not");
        body.Rows.Should().NotContain(row => row.MembershipId == former.Id);

        SeasonRow first = body.Rows.Single(row => row.MembershipId == scenario.MemberMembershipId);
        SeasonRow second = body.Rows.Single(row => row.MembershipId == scenario.SecondMemberMembershipId);
        SeasonRow late = body.Rows.Single(row => row.MembershipId == joiner.Id);

        first.TotalPoints.Should().Be(40);
        second.TotalPoints.Should().Be(40);
        first.Rank.Should().Be(1);
        second.Rank.Should().Be(1, "equal totals share a rank");
        first.PointsBehind.Should().Be(0);
        first.IsMe.Should().BeTrue();
        second.IsMe.Should().BeFalse();

        late.TotalPoints.Should().Be(25, "a mid-season joiner is totalled from their own weeks only");
        late.Rank.Should().Be(3, "the shared first place makes the next rank skip to 3");
        late.PointsBehind.Should().Be(15);
        late.WeeklyWins.Should().Be(1, "they had week 7's high score");

        first.WeeklyWins.Should().Be(0, "week 6 was won by the member who has since left");
        second.WeeklyWins.Should().Be(0);
    }

    [Fact]
    public async Task GivenNoScoredWeeks_WhenReadingTheSeasonLeaderboard_ThenThroughWeekIsNullAndEveryoneIsLevel()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        SeasonLeaderboard body = await GetSeasonAsync(scenario, scenario.MemberUserId);

        body.ThroughWeek.Should().BeNull();
        body.Rows.Should().HaveCount(3);
        body.Rows.Should().OnlyContain(row =>
            row.TotalPoints == 0 && row.PointsBehind == 0 && row.WeeklyWins == 0 && row.Rank == 1);
        body.Rows.Should().OnlyContain(row => row.Trend == StandingsTrend.None);
    }

    [Fact]
    public async Task GivenTwoCompletedWeeks_WhenReadingTheSeasonLeaderboard_ThenTrendArrowsComeFromTheSnapshots()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        await SeedSnapshotAsync(scenario.LeagueId, throughWeek: 6, scenario.MemberMembershipId, rank: 2, total: 20);
        await SeedSnapshotAsync(scenario.LeagueId, throughWeek: 6, scenario.SecondMemberMembershipId, rank: 1, total: 30);
        await SeedSnapshotAsync(scenario.LeagueId, throughWeek: 7, scenario.MemberMembershipId, rank: 1, total: 60);
        await SeedSnapshotAsync(scenario.LeagueId, throughWeek: 7, scenario.SecondMemberMembershipId, rank: 2, total: 40);

        SeasonLeaderboard body = await GetSeasonAsync(scenario, scenario.MemberUserId);

        body.Rows.Single(row => row.MembershipId == scenario.MemberMembershipId).Trend
            .Should().Be(StandingsTrend.Up);
        body.Rows.Single(row => row.MembershipId == scenario.SecondMemberMembershipId).Trend
            .Should().Be(StandingsTrend.Down);
        body.Rows.Single(row => row.MembershipId != scenario.MemberMembershipId
                && row.MembershipId != scenario.SecondMemberMembershipId).Trend
            .Should().Be(StandingsTrend.None, "the commissioner is in neither snapshot");
    }

    [Fact]
    public async Task GivenACompletedWeek_WhenReadingItsLeaderboard_ThenFormerMembersAppearAndTheWinnersAreMarked()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        Membership former = await AddMemberAsync(scenario, "Gone Away", joinedWeek: 1, removed: true);

        await MarkCompleteAsync(scenario.WeekGameSetId);
        await SeedResultAsync(scenario.WeekGameSetId, scenario.MemberMembershipId, points: 30, correct: 9, activeGames: 12);
        await SeedResultAsync(scenario.WeekGameSetId, scenario.SecondMemberMembershipId, points: 10, correct: 4, activeGames: 12);
        await SeedResultAsync(scenario.WeekGameSetId, former.Id, points: 30, correct: 10, activeGames: 12);

        WeekLeaderboard body = await GetWeekAsync(scenario, PickWeekScenario.Week, scenario.MemberUserId);

        body.Week.Should().Be(PickWeekScenario.Week);
        body.IsComplete.Should().BeTrue();
        body.Rows.Should().HaveCount(3, "everyone with a result row is listed, former members included");

        WeekRow mine = body.Rows.Single(row => row.MembershipId == scenario.MemberMembershipId);
        WeekRow gone = body.Rows.Single(row => row.MembershipId == former.Id);
        WeekRow other = body.Rows.Single(row => row.MembershipId == scenario.SecondMemberMembershipId);

        mine.Rank.Should().Be(1);
        gone.Rank.Should().Be(1, "tied points share the rank");
        other.Rank.Should().Be(3);

        mine.IsWinner.Should().BeTrue();
        gone.IsWinner.Should().BeTrue("ties share the weekly win");
        other.IsWinner.Should().BeFalse();

        gone.IsFormer.Should().BeTrue();
        mine.IsFormer.Should().BeFalse();
        mine.IsMe.Should().BeTrue();
        mine.Correct.Should().Be(9);
        mine.Total.Should().Be(12);
    }

    [Fact]
    public async Task GivenAWeekStillBeingPlayed_WhenReadingItsLeaderboard_ThenNobodyIsAWinnerYet()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        await SeedResultAsync(
            scenario.WeekGameSetId, scenario.MemberMembershipId, points: 30, isWeekComplete: false);

        WeekLeaderboard body = await GetWeekAsync(scenario, PickWeekScenario.Week, scenario.MemberUserId);

        body.IsComplete.Should().BeFalse("the client labels the week provisional from this flag");
        body.Rows.Single().IsWinner.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAWeekWithNoGameSet_WhenReadingItsLeaderboard_ThenItIsEmpty()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        WeekLeaderboard body = await GetWeekAsync(scenario, week: 3, scenario.MemberUserId);

        body.Week.Should().Be(3);
        body.IsComplete.Should().BeFalse();
        body.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenAWeekOutsideTheLeaguesRange_WhenReadingItsLeaderboard_ThenItIs404()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/40/leaderboard");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenAnUnlockedWeek_WhenReadingTheGrid_ThenItIs403()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await member.GetAsync(GridRoute(scenario));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PicksNotVisible");
    }

    [Fact]
    public async Task GivenALockedWeek_WhenReadingTheGrid_ThenEveryCellCarriesItsOutcome()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient first = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);

        GameSetGameDto decided = scenario.Games[0];
        GameSetGameDto voided = scenario.Games[1];
        GameSetGameDto pending = scenario.Games[2];

        await PickAsync(first, scenario, decided, decided.HomeTeam.TeamId);
        await PickAsync(second, scenario, decided, decided.AwayTeam.TeamId);
        await PickAsync(first, scenario, voided, voided.HomeTeam.TeamId);
        await PickAsync(first, scenario, pending, pending.HomeTeam.TeamId);

        // A result override and a void, rather than scores on the shared fixture Games rows, so
        // this league's week is decided without touching every other league's copy of the game.
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            WeekGameSetGame decidedRow = await db.WeekGameSetGames.SingleAsync(
                row => row.Id == decided.GameSetGameId!.Value);
            decidedRow.ResultOverrideWinnerTeamId = decided.HomeTeam.TeamId;

            WeekGameSetGame voidedRow = await db.WeekGameSetGames.SingleAsync(
                row => row.Id == voided.GameSetGameId!.Value);
            voidedRow.IsVoided = true;

            await db.SaveChangesAsync();
        });

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        // Removed after lock: they still played the week, so the grid keeps their column, flagged.
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            Membership leaving = await db.Memberships.SingleAsync(m => m.Id == scenario.SecondMemberMembershipId);
            leaving.RemovedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime;
            await db.SaveChangesAsync();
        });

        // The Games rows are shared by every league in the run's one database, and other suites
        // advance the fixture score snapshots, so "no winner yet" has to be asserted from a status
        // this test controls - and put back afterwards (AGENT-NOTES, "Game sets and points").
        GameScoreState originalPending = await ReadGameScoreAsync(pending.GameId);
        await WriteGameScoreAsync(pending.GameId, new GameScoreState(GameStatus.Scheduled, null, null));

        try
        {
            using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);
            using HttpResponseMessage response = await member.GetAsync(GridRoute(scenario));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            WeekGrid? body = await response.Content.ReadFromJsonAsync<WeekGrid>();

            body!.Games.Should().HaveCount(3, "a voided game stays on the grid, greyed out");
            body.Members.Should().HaveCount(2, "only the two members with a submission row played the week");
            body.Members.Single(column => column.MembershipId == scenario.SecondMemberMembershipId).IsFormer
                .Should().BeTrue();
            body.Cells.Should().HaveCount(6);

            Cell(body, decided, scenario.MemberMembershipId).Outcome.Should().Be(GridOutcome.Correct);
            Cell(body, decided, scenario.MemberMembershipId).TeamId.Should().Be(decided.HomeTeam.TeamId);
            Cell(body, decided, scenario.SecondMemberMembershipId).Outcome.Should().Be(GridOutcome.Incorrect);
            Cell(body, voided, scenario.MemberMembershipId).Outcome.Should().Be(GridOutcome.Voided);
            Cell(body, voided, scenario.SecondMemberMembershipId).Outcome.Should().Be(GridOutcome.Voided);
            Cell(body, pending, scenario.MemberMembershipId).Outcome.Should().Be(GridOutcome.Pending);
            Cell(body, pending, scenario.SecondMemberMembershipId).Outcome.Should().Be(GridOutcome.NoPick);
        }
        finally
        {
            await WriteGameScoreAsync(pending.GameId, originalPending);
        }
    }

    [Fact]
    public async Task GivenAMemberWhoPickedThenLeftBeforeLock_WhenReadingTheGrid_ThenTheyHaveNoColumn()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);
        using HttpClient first = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.MemberUserId);
        using HttpClient second = _fixture.PinnedFactory.CreateMutatingClientAs(scenario.SecondMemberUserId);

        GameSetGameDto game = scenario.Games[0];
        await PickAsync(first, scenario, game, game.HomeTeam.TeamId);
        await PickAsync(second, scenario, game, game.AwayTeam.TeamId);

        // Gone before the lock, so the lock job never settles their row (D-111): "has a
        // WeekSubmissions row" would still column them, the status rule (D-135) does not.
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            Membership leaving = await db.Memberships.SingleAsync(m => m.Id == scenario.SecondMemberMembershipId);
            leaving.RemovedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime;
            await db.SaveChangesAsync();
        });

        await scenario.MarkLockedAsync(_fixture.PinnedFactory, ApiTestFixture.PinnedNowUtc.UtcDateTime);

        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);
        using HttpResponseMessage response = await member.GetAsync(GridRoute(scenario));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekGrid? body = await response.Content.ReadFromJsonAsync<WeekGrid>();

        body!.Members.Should().ContainSingle(column => column.MembershipId == scenario.MemberMembershipId);
        body.Members.Should().NotContain(column => column.MembershipId == scenario.SecondMemberMembershipId);
        body.Cells.Should().OnlyContain(cell => cell.MembershipId == scenario.MemberMembershipId);
    }

    [Fact]
    public async Task GivenAWeekWhoseSetHasNoGames_WhenListingLeagueWeeks_ThenItIsNotNavigable()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        // The row a commissioner's first visit to a week creates, before anything is generated.
        await SeedWeekSetAsync(scenario.LeagueId, week: 5, isComplete: false);

        using HttpClient member = _fixture.PinnedFactory.CreateClientAs(scenario.MemberUserId);
        using HttpResponseMessage response = await member.GetAsync($"/api/leagues/{scenario.LeagueId}/weeks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LeagueWeek[]? weeks = await response.Content.ReadFromJsonAsync<LeagueWeek[]>();

        weeks!.Single(week => week.Week == PickWeekScenario.Week).HasGameSet.Should().BeTrue();
        weeks!.Single(week => week.Week == 5).HasGameSet.Should().BeFalse(
            "a WeekGameSets row with no games has not been generated");
    }

    private static string GridRoute(PickWeekScenario scenario) =>
        $"/api/leagues/{scenario.LeagueId}/weeks/{PickWeekScenario.Week}/grid";

    private static GridCell Cell(WeekGrid grid, GameSetGameDto game, Guid membershipId) =>
        grid.Cells.Single(cell =>
            cell.GameSetGameId == game.GameSetGameId!.Value && cell.MembershipId == membershipId);

    private static async Task PickAsync(
        HttpClient client,
        PickWeekScenario scenario,
        GameSetGameDto game,
        Guid teamId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{scenario.PicksRoute}/me/{game.GameId}", new SetPickRequest(teamId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<SeasonLeaderboard> GetSeasonAsync(PickWeekScenario scenario, Guid callerUserId)
    {
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(callerUserId);
        using HttpResponseMessage response = await client.GetAsync($"/api/leagues/{scenario.LeagueId}/leaderboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SeasonLeaderboard>())!;
    }

    private async Task<WeekLeaderboard> GetWeekAsync(PickWeekScenario scenario, int week, Guid callerUserId)
    {
        using HttpClient client = _fixture.PinnedFactory.CreateClientAs(callerUserId);
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/leagues/{scenario.LeagueId}/weeks/{week}/leaderboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WeekLeaderboard>())!;
    }

    private async Task<Membership> AddMemberAsync(
        PickWeekScenario scenario,
        string displayName,
        int joinedWeek,
        bool removed = false)
    {
        string unique = $"{displayName} {Guid.CreateVersion7().ToString()[..8]}";

        return await _fixture.PinnedFactory.QueryDbAsync(async db =>
        {
            User user = await TestUsers.CreateUserAsync(db, unique);
            var membership = new Membership
            {
                Id = Guid.CreateVersion7(),
                LeagueId = scenario.LeagueId,
                UserId = user.Id,
                Role = MembershipRole.Member,
                JoinedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
                JoinedWeek = joinedWeek,
                RemovedUtc = removed ? ApiTestFixture.PinnedNowUtc.UtcDateTime : null,
            };

            db.Memberships.Add(membership);
            await db.SaveChangesAsync();
            return membership;
        });
    }

    private async Task<GameScoreState> ReadGameScoreAsync(Guid gameId) =>
        await _fixture.PinnedFactory.QueryDbAsync(async db =>
        {
            Game game = await db.Games.AsNoTracking().SingleAsync(candidate => candidate.Id == gameId);
            return new GameScoreState(game.Status, game.HomeScore, game.AwayScore);
        });

    private async Task WriteGameScoreAsync(Guid gameId, GameScoreState state) =>
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            Game game = await db.Games.SingleAsync(candidate => candidate.Id == gameId);
            game.Status = state.Status;
            game.HomeScore = state.HomeScore;
            game.AwayScore = state.AwayScore;
            await db.SaveChangesAsync();
        });

    private async Task<Guid> SeedWeekSetAsync(Guid leagueId, int week, bool isComplete)
    {
        var set = new WeekGameSet
        {
            Id = Guid.CreateVersion7(),
            LeagueId = leagueId,
            Week = week,
            UsesOverride = false,
            GeneratedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
            IsComplete = isComplete,
        };

        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(set);
            await db.SaveChangesAsync();
        });

        return set.Id;
    }

    private async Task MarkCompleteAsync(Guid weekGameSetId) =>
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            WeekGameSet set = await db.WeekGameSets.SingleAsync(candidate => candidate.Id == weekGameSetId);
            set.IsComplete = true;
            await db.SaveChangesAsync();
        });

    private async Task SeedResultAsync(
        Guid weekGameSetId,
        Guid membershipId,
        int points,
        int correct = 0,
        int activeGames = 12,
        bool isWeekComplete = true) =>
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            db.WeekResults.Add(new WeekResult
            {
                MembershipId = membershipId,
                WeekGameSetId = weekGameSetId,
                Points = points,
                CorrectCount = correct,
                ActiveGameCount = activeGames,
                IsWeekComplete = isWeekComplete,
                ComputedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
            });

            await db.SaveChangesAsync();
        });

    private async Task SeedSnapshotAsync(Guid leagueId, int throughWeek, Guid membershipId, int rank, int total) =>
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            db.SeasonStandingsSnapshots.Add(new SeasonStandingsSnapshot
            {
                LeagueId = leagueId,
                ThroughWeek = throughWeek,
                MembershipId = membershipId,
                Rank = rank,
                TotalPoints = total,
            });

            await db.SaveChangesAsync();
        });

    /// <summary>The bits of a shared fixture <c>Games</c> row a test borrows and must put back.</summary>
    private sealed record GameScoreState(GameStatus Status, int? HomeScore, int? AwayScore);
}
