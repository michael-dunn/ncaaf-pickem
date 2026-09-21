using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.Leaderboard;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// Feature 07's performance criterion: "the season leaderboard loads in under 1 second for a league
/// of up to 50 members and 15 weeks". The league is seeded under its own synthetic season year so
/// nothing here disturbs the 2026 fixture week every other suite shares.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class LeaderboardPerfTests
{
    private const int SeasonYear = 2096;
    private const int MemberCount = 50;
    private const int WeekCount = 15;
    private const int GamesPerWeek = 20;

    private readonly ApiTestFixture _fixture;

    public LeaderboardPerfTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenFiftyMembersAcrossFifteenWeeks_WhenReadingTheSeasonLeaderboard_ThenItAnswersInUnderASecond()
    {
        PerfLeague league = await SeedLeagueAsync();

        using HttpClient client = _fixture.Factory.CreateClientAs(league.ViewerUserId);
        string route = $"/api/leagues/{league.LeagueId}/leaderboard";

        // One warm-up call: the first request through any EF query pays for compiling it, which is
        // a per-process cost the real app pays once at startup, not per member.
        using (HttpResponseMessage warmup = await client.GetAsync(route))
        {
            warmup.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await client.GetAsync(route);
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SeasonLeaderboard? body = await response.Content.ReadFromJsonAsync<SeasonLeaderboard>();

        body!.Rows.Should().HaveCount(MemberCount);
        body.ThroughWeek.Should().Be(WeekCount);
        body.Rows[0].Rank.Should().Be(1);
        body.Rows.Should().Contain(row => row.IsMe);
        body.Rows.Should().Contain(row => row.Trend != StandingsTrend.None, "two completed weeks produce arrows");

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(1),
            "Feature 07 requires the season leaderboard under a second for 50 members over 15 weeks (took {0} ms, budget 1000)",
            stopwatch.ElapsedMilliseconds);

        // The same league's heaviest week, measured off the same seed rather than a second one.
        string weekRoute = $"/api/leagues/{league.LeagueId}/weeks/{WeekCount}/leaderboard";
        using (HttpResponseMessage warmup = await client.GetAsync(weekRoute))
        {
            warmup.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var weekStopwatch = Stopwatch.StartNew();
        using HttpResponseMessage weekResponse = await client.GetAsync(weekRoute);
        weekStopwatch.Stop();

        weekResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        WeekLeaderboard? week = await weekResponse.Content.ReadFromJsonAsync<WeekLeaderboard>();

        week!.Rows.Should().HaveCount(MemberCount);
        weekStopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(1),
            "a week leaderboard is a smaller query than the season one (took {0} ms)",
            weekStopwatch.ElapsedMilliseconds);
    }

    private async Task<PerfLeague> SeedLeagueAsync()
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.Factory);

        Guid leagueId = Guid.CreateVersion7();
        Guid viewerUserId = Guid.Empty;

        await _fixture.Factory.ExecuteDbAsync(async db =>
        {
            Guid[] teamIds = await db.Teams
                .AsNoTracking()
                .OrderBy(team => team.School)
                .Select(team => team.Id)
                .Take(GamesPerWeek * 2)
                .ToArrayAsync();

            teamIds.Length.Should().BeGreaterThanOrEqualTo(4, "the fixture seeds enough teams to pair up");

            var creator = new User
            {
                Id = Guid.CreateVersion7(),
                ExternalSubject = $"test-perf-{leagueId:N}",
                Email = $"perf-{leagueId:N}@test.local",
                DisplayName = $"Perf Creator {leagueId.ToString()[..8]}",
                CreatedUtc = DateTime.UtcNow,
                LastLoginUtc = DateTime.UtcNow,
            };
            db.Users.Add(creator);

            db.Leagues.Add(new League
            {
                Id = leagueId,
                Name = $"Perf League {leagueId.ToString()[..8]}",
                SeasonYear = SeasonYear,
                FirstWeek = 1,
                LastWeek = WeekCount,
                DefaultPointValue = 10,
                CreatedByUserId = creator.Id,
                CreatedUtc = DateTime.UtcNow,
            });

            var membershipIds = new Guid[MemberCount];
            for (int i = 0; i < MemberCount; i++)
            {
                Guid userId = Guid.CreateVersion7();
                db.Users.Add(new User
                {
                    Id = userId,
                    ExternalSubject = $"test-perf-{userId:N}",
                    Email = $"perf-{userId:N}@test.local",
                    DisplayName = $"Perf Member {i:D2} {userId.ToString()[..8]}",
                    CreatedUtc = DateTime.UtcNow,
                    LastLoginUtc = DateTime.UtcNow,
                });

                membershipIds[i] = Guid.CreateVersion7();
                db.Memberships.Add(new Membership
                {
                    Id = membershipIds[i],
                    LeagueId = leagueId,
                    UserId = userId,
                    Role = i == 0 ? MembershipRole.Commissioner : MembershipRole.Member,
                    JoinedUtc = DateTime.UtcNow,
                    JoinedWeek = 1,
                });

                if (i == 0)
                {
                    viewerUserId = userId;
                }
            }

            long cfbdGameId = (SeasonYear * 1_000_000L) + 1;
            for (int week = 1; week <= WeekCount; week++)
            {
                Guid setId = Guid.CreateVersion7();
                db.WeekGameSets.Add(new WeekGameSet
                {
                    Id = setId,
                    LeagueId = leagueId,
                    Week = week,
                    UsesOverride = false,
                    GeneratedUtc = DateTime.UtcNow,
                    LockAtUtc = DateTime.UtcNow,
                    LockedUtc = DateTime.UtcNow,
                    IsComplete = true,
                });

                for (int game = 0; game < GamesPerWeek; game++)
                {
                    Guid gameId = Guid.CreateVersion7();
                    db.Games.Add(new Game
                    {
                        Id = gameId,
                        CfbdGameId = cfbdGameId++,
                        SeasonYear = SeasonYear,
                        Week = week,
                        HomeTeamId = teamIds[(game * 2) % teamIds.Length],
                        AwayTeamId = teamIds[((game * 2) + 1) % teamIds.Length],
                        KickoffUtc = new DateTime(2096, 9, 5, 16, 0, 0, DateTimeKind.Utc).AddDays(week * 7),
                        KickoffEasternDate = new DateOnly(2096, 9, 5).AddDays(week * 7),
                        IsSaturdayEastern = true,
                        Status = GameStatus.Final,
                        HomeScore = 28,
                        AwayScore = 21,
                    });

                    db.WeekGameSetGames.Add(new WeekGameSetGame
                    {
                        Id = Guid.CreateVersion7(),
                        WeekGameSetId = setId,
                        GameId = gameId,
                        Source = GameSetGameSource.Rule,
                        AddedUtc = DateTime.UtcNow,
                        ResolvedPointValue = 10,
                    });
                }

                for (int member = 0; member < MemberCount; member++)
                {
                    db.WeekResults.Add(new WeekResult
                    {
                        MembershipId = membershipIds[member],
                        WeekGameSetId = setId,
                        Points = ((week * 7) + (member * 3)) % 140,
                        CorrectCount = (week + member) % GamesPerWeek,
                        ActiveGameCount = GamesPerWeek,
                        IsWeekComplete = true,
                        ComputedUtc = DateTime.UtcNow,
                    });

                    // Every week is complete, so every week has a snapshot - the worst case for the
                    // trend query, which only ever looks at the newest two.
                    db.SeasonStandingsSnapshots.Add(new SeasonStandingsSnapshot
                    {
                        LeagueId = leagueId,
                        ThroughWeek = week,
                        MembershipId = membershipIds[member],
                        Rank = member + 1,
                        TotalPoints = week * 10,
                    });
                }
            }

            await db.SaveChangesAsync();
        });

        return new PerfLeague(leagueId, viewerUserId);
    }

    private sealed record PerfLeague(Guid LeagueId, Guid ViewerUserId);
}
