using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Infrastructure.Scoring;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The real <see cref="IStandingsSnapshotWriter"/> (P5-03): what P5-01's <c>ScoringService</c>
/// calls when a week first becomes complete, and what the leaderboard's trend arrows then read.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class StandingsSnapshotWriterTests
{
    private readonly ApiTestFixture _fixture;

    public StandingsSnapshotWriterTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GivenTheApp_WhenResolvingTheSnapshotWriter_ThenItIsTheRealOneNotTheNoOp()
    {
        using IServiceScope scope = _fixture.Factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IStandingsSnapshotWriter>()
            .Should().BeOfType<StandingsSnapshotWriter>(
                "the real writer is registered above P5-01's TryAddScoped no-op");
    }

    [Fact]
    public async Task GivenACompletedWeek_WhenWritingTheSnapshot_ThenEveryActiveMemberIsRankedThroughThatWeek()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        Guid weekSix = await SeedWeekSetAsync(scenario.LeagueId, week: 6);
        await SeedResultAsync(weekSix, scenario.MemberMembershipId, points: 30);
        await SeedResultAsync(weekSix, scenario.SecondMemberMembershipId, points: 30);
        await SeedResultAsync(scenario.WeekGameSetId, scenario.MemberMembershipId, points: 20);

        // Week 7 exists but is not part of the through-week-6 snapshot.
        await WriteSnapshotAsync(scenario.LeagueId, throughWeek: 6);

        SeasonStandingsSnapshot[] rows = await ReadSnapshotAsync(scenario.LeagueId, throughWeek: 6);

        rows.Should().HaveCount(3, "one row per active membership, the commissioner included");
        rows.Single(row => row.MembershipId == scenario.MemberMembershipId).TotalPoints.Should().Be(
            30, "the week 7 result is above the through-week");
        rows.Single(row => row.MembershipId == scenario.MemberMembershipId).Rank.Should().Be(1);
        rows.Single(row => row.MembershipId == scenario.SecondMemberMembershipId).Rank.Should().Be(
            1, "equal totals share the rank the leaderboard would give them");
        rows.Single(row => row.MembershipId != scenario.MemberMembershipId
            && row.MembershipId != scenario.SecondMemberMembershipId).Rank.Should().Be(3);
    }

    [Fact]
    public async Task GivenASnapshotThatAlreadyExists_WhenARescoreWritesItAgain_ThenItIsUpdatedNotDuplicated()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        await SeedResultAsync(scenario.WeekGameSetId, scenario.MemberMembershipId, points: 10);
        await SeedResultAsync(scenario.WeekGameSetId, scenario.SecondMemberMembershipId, points: 40);

        await WriteSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week);

        (await ReadSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week))
            .Single(row => row.MembershipId == scenario.MemberMembershipId).Rank.Should().Be(2);

        // A correction: the first member's week is rescored ahead of the second's.
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            WeekResult result = await db.WeekResults.SingleAsync(row =>
                row.WeekGameSetId == scenario.WeekGameSetId
                && row.MembershipId == scenario.MemberMembershipId);
            result.Points = 60;
            await db.SaveChangesAsync();
        });

        await WriteSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week);

        SeasonStandingsSnapshot[] rows = await ReadSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week);

        rows.Should().HaveCount(3, "writing twice upserts rather than duplicating");
        rows.Single(row => row.MembershipId == scenario.MemberMembershipId).Rank.Should().Be(1);
        rows.Single(row => row.MembershipId == scenario.MemberMembershipId).TotalPoints.Should().Be(60);
    }

    [Fact]
    public async Task GivenAMemberRemovedSinceTheLastSnapshot_WhenWritingItAgain_ThenTheirRowGoes()
    {
        PickWeekScenario scenario = await PickWeekScenario.CreateAsync(_fixture.PinnedFactory);

        await SeedResultAsync(scenario.WeekGameSetId, scenario.SecondMemberMembershipId, points: 25);
        await WriteSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week);

        (await ReadSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week))
            .Should().Contain(row => row.MembershipId == scenario.SecondMemberMembershipId);

        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            Membership leaving = await db.Memberships.SingleAsync(m => m.Id == scenario.SecondMemberMembershipId);
            leaving.RemovedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime;
            await db.SaveChangesAsync();
        });

        await WriteSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week);

        SeasonStandingsSnapshot[] rows = await ReadSnapshotAsync(scenario.LeagueId, PickWeekScenario.Week);

        rows.Should().HaveCount(2);
        rows.Should().NotContain(row => row.MembershipId == scenario.SecondMemberMembershipId,
            "the arrows compare the active standings, which no longer hold them");
    }

    private async Task WriteSnapshotAsync(Guid leagueId, int throughWeek)
    {
        await using AsyncServiceScope scope = _fixture.PinnedFactory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<IStandingsSnapshotWriter>()
            .WriteSnapshotAsync(leagueId, throughWeek, CancellationToken.None);
    }

    private async Task<SeasonStandingsSnapshot[]> ReadSnapshotAsync(Guid leagueId, int throughWeek) =>
        await _fixture.PinnedFactory.QueryDbAsync(async db => await db.SeasonStandingsSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.LeagueId == leagueId && snapshot.ThroughWeek == throughWeek)
            .ToArrayAsync());

    private async Task<Guid> SeedWeekSetAsync(Guid leagueId, int week)
    {
        var set = new WeekGameSet
        {
            Id = Guid.CreateVersion7(),
            LeagueId = leagueId,
            Week = week,
            UsesOverride = false,
            GeneratedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
            IsComplete = true,
        };

        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(set);
            await db.SaveChangesAsync();
        });

        return set.Id;
    }

    private async Task SeedResultAsync(Guid weekGameSetId, Guid membershipId, int points) =>
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            db.WeekResults.Add(new WeekResult
            {
                MembershipId = membershipId,
                WeekGameSetId = weekGameSetId,
                Points = points,
                CorrectCount = 0,
                ActiveGameCount = 12,
                IsWeekComplete = true,
                ComputedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
            });

            await db.SaveChangesAsync();
        });
}
