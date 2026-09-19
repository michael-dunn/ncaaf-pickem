using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Jobs;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <see cref="EnsureCurrentWeekSetsJob"/> (Feature 02, P3-04): "members always have a set"
/// without a commissioner ever visiting the config page. Driven against
/// <see cref="ApiTestFixture.PinnedFactory"/> so "current week" is deterministically week 7.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AutoCreateWeekSetTests
{
    private readonly ApiTestFixture _fixture;

    public AutoCreateWeekSetTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<League> CreateLeagueAsync(int firstWeek = 1, int lastWeek = 14)
    {
        await FixtureGameData.EnsureSeededAsync(_fixture.PinnedFactory);

        User commissioner = await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, "AutoCreateCommish"));
        League league = await _fixture.PinnedFactory.QueryDbAsync(async db =>
        {
            League created = await TestUsers.CreateLeagueAsync(db, commissioner);
            created.FirstWeek = firstWeek;
            created.LastWeek = lastWeek;
            await db.SaveChangesAsync();
            return created;
        });
        await _fixture.PinnedFactory.QueryDbAsync(db => TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));
        return league;
    }

    private async Task RunJobAsync()
    {
        await using AsyncServiceScope scope = _fixture.PinnedFactory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        EnsureCurrentWeekSetsJob job = ActivatorUtilities.CreateInstance<EnsureCurrentWeekSetsJob>(scope.ServiceProvider);
        await job.RunAsync(ApiTestFixture.PinnedNowUtc, CancellationToken.None);
    }

    private Task<WeekGameSet?> FindSetAsync(Guid leagueId, int week) =>
        _fixture.PinnedFactory.QueryDbAsync(db => db.WeekGameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Week == week));

    [Fact]
    public async Task GivenALeagueWithNoRowForTheCurrentWeek_WhenTheJobRuns_ThenOneIsCreatedFromDefaultRules()
    {
        League league = await CreateLeagueAsync();

        (await FindSetAsync(league.Id, ApiTestFixture.PinnedCurrentWeek)).Should().BeNull();

        await RunJobAsync();

        WeekGameSet? set = await FindSetAsync(league.Id, ApiTestFixture.PinnedCurrentWeek);
        set.Should().NotBeNull();
        set!.UsesOverride.Should().BeFalse();
        set.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GivenALeagueThatAlreadyHasARowForTheCurrentWeek_WhenTheJobRuns_ThenItIsLeftUntouched()
    {
        League league = await CreateLeagueAsync();

        DateTime generatedAt = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            db.WeekGameSets.Add(new WeekGameSet
            {
                Id = Guid.CreateVersion7(),
                LeagueId = league.Id,
                Week = ApiTestFixture.PinnedCurrentWeek,
                UsesOverride = true,
                GeneratedUtc = generatedAt,
            });
            await db.SaveChangesAsync();
        });

        await RunJobAsync();

        WeekGameSet? set = await FindSetAsync(league.Id, ApiTestFixture.PinnedCurrentWeek);
        set.Should().NotBeNull();
        set!.UsesOverride.Should().BeTrue("the job must never touch a week that already has a row");
        set.GeneratedUtc.Should().Be(generatedAt);
    }

    [Fact]
    public async Task GivenALeagueWhoseFirstWeekIsAfterTheCurrentWeek_WhenTheJobRuns_ThenNothingIsCreated()
    {
        League league = await CreateLeagueAsync(firstWeek: ApiTestFixture.PinnedCurrentWeek + 1, lastWeek: 14);

        await RunJobAsync();

        (await FindSetAsync(league.Id, ApiTestFixture.PinnedCurrentWeek)).Should().BeNull();
    }

    [Fact]
    public async Task GivenALeagueWhoseLastWeekIsBeforeTheCurrentWeek_WhenTheJobRuns_ThenNothingIsCreated()
    {
        League league = await CreateLeagueAsync(firstWeek: 1, lastWeek: ApiTestFixture.PinnedCurrentWeek - 1);

        await RunJobAsync();

        (await FindSetAsync(league.Id, ApiTestFixture.PinnedCurrentWeek)).Should().BeNull();
    }

    [Fact]
    public async Task GivenACompletedLeague_WhenTheJobRuns_ThenNothingIsCreated()
    {
        League league = await CreateLeagueAsync();
        await _fixture.PinnedFactory.ExecuteDbAsync(async db =>
        {
            await db.Leagues.Where(l => l.Id == league.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.IsComplete, true));
        });

        await RunJobAsync();

        (await FindSetAsync(league.Id, ApiTestFixture.PinnedCurrentWeek)).Should().BeNull();
    }
}
