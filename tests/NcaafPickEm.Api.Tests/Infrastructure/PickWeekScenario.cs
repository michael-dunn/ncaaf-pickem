using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// A league with a commissioner and two members whose Week 7, 2026 game set has already been
/// generated from the Top 25 rule — the starting point every picks test needs.
/// </summary>
/// <param name="LeagueId">The league.</param>
/// <param name="CommissionerUserId">Commissioner's user id.</param>
/// <param name="MemberUserId">First member's user id.</param>
/// <param name="MemberMembershipId">First member's membership id.</param>
/// <param name="SecondMemberUserId">Second member's user id.</param>
/// <param name="SecondMemberMembershipId">Second member's membership id.</param>
/// <param name="WeekGameSetId">The generated <c>WeekGameSets.Id</c> for week 7.</param>
/// <param name="Games">The generated games, ordered by kickoff.</param>
public sealed record PickWeekScenario(
    Guid LeagueId,
    Guid CommissionerUserId,
    Guid MemberUserId,
    Guid MemberMembershipId,
    Guid SecondMemberUserId,
    Guid SecondMemberMembershipId,
    Guid WeekGameSetId,
    GameSetGameDto[] Games)
{
    /// <summary>The week every scenario is built for: the fixture week (<see cref="ApiTestFixture.PinnedCurrentWeek"/>).</summary>
    public const int Week = ApiTestFixture.PinnedCurrentWeek;

    /// <summary>Route prefix for the picks endpoints of this league's fixture week.</summary>
    public string PicksRoute => $"/api/leagues/{LeagueId}/weeks/{Week}/picks";

    /// <summary>
    /// Seeds the fixture reference data, a fresh league, its members, and a generated week 7 set.
    /// </summary>
    /// <param name="factory">
    /// The app to seed through. Pass a factory whose clock is inside week 7
    /// (<see cref="ApiTestFixture.PinnedFactory"/>), or the current-week guard refuses every pick.
    /// </param>
    public static async Task<PickWeekScenario> CreateAsync(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await FixtureGameData.EnsureSeededAsync(factory);

        User commissioner = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Commish {Suffix()}"));
        User member = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Member {Suffix()}"));
        User second = await factory.QueryDbAsync(db => TestUsers.CreateUserAsync(db, $"Second {Suffix()}"));

        League league = await factory.QueryDbAsync(db => TestUsers.CreateLeagueAsync(db, commissioner));
        await factory.QueryDbAsync(db =>
            TestUsers.CreateMembershipAsync(db, league, commissioner, MembershipRole.Commissioner));
        Membership memberMembership = await factory.QueryDbAsync(db =>
            TestUsers.CreateMembershipAsync(db, league, member));
        Membership secondMembership = await factory.QueryDbAsync(db =>
            TestUsers.CreateMembershipAsync(db, league, second));

        using HttpClient commish = factory.CreateMutatingClientAs(commissioner.Id);

        GameSetRuleDto[] rules = [new(null, GameSetRuleType.Top25, null, null, null, null, false, 0)];
        using (HttpResponseMessage put = await commish.PutAsJsonAsync($"/api/leagues/{league.Id}/gameset-rules", rules))
        {
            put.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        GameSetGameDto[] games;
        using (HttpResponseMessage generate = await commish.PostAsync(
            $"/api/leagues/{league.Id}/weeks/{Week}/gameset/generate", null))
        {
            generate.StatusCode.Should().Be(HttpStatusCode.OK);
            WeekGameSetResponse? body = await generate.Content.ReadFromJsonAsync<WeekGameSetResponse>();
            games = body!.Games;
        }

        Guid setId = await factory.QueryDbAsync(db => db.WeekGameSets
            .Where(set => set.LeagueId == league.Id && set.Week == Week)
            .Select(set => set.Id)
            .FirstAsync());

        return new PickWeekScenario(
            league.Id,
            commissioner.Id,
            member.Id,
            memberMembership.Id,
            second.Id,
            secondMembership.Id,
            setId,
            games);
    }

    /// <summary>Marks the week locked the way P4-02's job will, without running the job.</summary>
    /// <param name="factory">The app whose database to write to.</param>
    /// <param name="lockedUtc">The instant to record as <c>LockedUtc</c>.</param>
    public async Task MarkLockedAsync(ApiFactory factory, DateTime lockedUtc)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await factory.ExecuteDbAsync(async db =>
        {
            WeekGameSet set = await db.WeekGameSets.SingleAsync(candidate => candidate.Id == WeekGameSetId);
            set.LockedUtc = lockedUtc;
            await db.SaveChangesAsync();
        });
    }

    private static string Suffix() => Guid.CreateVersion7().ToString()[..8];
}
