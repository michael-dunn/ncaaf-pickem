using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Users;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Seeds the rows an authorization test needs: users, a league, and memberships.
/// </summary>
/// <remarks>
/// One database is shared by the whole run, so every helper here invents unique names and never
/// assumes an empty table.
/// </remarks>
public static class TestUsers
{
    /// <summary>Inserts a user and returns it.</summary>
    public static async Task<User> CreateUserAsync(AppDbContext database, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        Guid id = Guid.CreateVersion7();
        var user = new User
        {
            Id = id,
            ExternalSubject = $"test-{id:N}",
            Email = $"{id:N}@test.local",
            DisplayName = displayName ?? $"User {id.ToString()[..8]}",
            CreatedUtc = DateTime.UtcNow,
            LastLoginUtc = DateTime.UtcNow,
        };

        database.Users.Add(user);
        await database.SaveChangesAsync();
        return user;
    }

    /// <summary>Inserts a league created by <paramref name="createdBy"/> and returns it.</summary>
    public static async Task<League> CreateLeagueAsync(AppDbContext database, User createdBy)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(createdBy);

        var league = new League
        {
            Id = Guid.CreateVersion7(),
            Name = $"League {Guid.CreateVersion7().ToString()[..8]}",
            SeasonYear = 2026,
            FirstWeek = 1,
            LastWeek = 14,
            DefaultPointValue = 10,
            CreatedByUserId = createdBy.Id,
            CreatedUtc = DateTime.UtcNow,
        };

        database.Leagues.Add(league);
        await database.SaveChangesAsync();
        return league;
    }

    /// <summary>
    /// Inserts a membership. Pass <paramref name="removed"/> to seed a former member. Joined (and
    /// removed) at <see cref="ApiTestFixture.PinnedNowUtc"/>, inside week 7 before its lock, not at
    /// the wall clock: the lock job and the all-picks view only count members who joined by
    /// <c>LockAtUtc</c> (D-111), so a wall-clock stamp turned every helper member into a late joiner
    /// once the real date passed the fixture week's first kickoff (2026-10-17).
    /// </summary>
    public static async Task<Membership> CreateMembershipAsync(
        AppDbContext database,
        League league,
        User user,
        MembershipRole role = MembershipRole.Member,
        bool removed = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(user);

        var membership = new Membership
        {
            Id = Guid.CreateVersion7(),
            LeagueId = league.Id,
            UserId = user.Id,
            Role = role,
            JoinedUtc = ApiTestFixture.PinnedNowUtc.UtcDateTime,
            JoinedWeek = 1,
            RemovedUtc = removed ? ApiTestFixture.PinnedNowUtc.UtcDateTime : null,
        };

        database.Memberships.Add(membership);
        await database.SaveChangesAsync();
        return membership;
    }

    /// <summary>
    /// Seeds a league with one commissioner, one member, and an unrelated non-member.
    /// </summary>
    public static async Task<LeagueScenario> CreateLeagueScenarioAsync(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return await factory.QueryDbAsync(async database =>
        {
            User commissioner = await CreateUserAsync(database, "Commish");
            User member = await CreateUserAsync(database, "Member");
            User stranger = await CreateUserAsync(database, "Stranger");

            League league = await CreateLeagueAsync(database, commissioner);
            await CreateMembershipAsync(database, league, commissioner, MembershipRole.Commissioner);
            await CreateMembershipAsync(database, league, member);

            return new LeagueScenario(league.Id, commissioner.Id, member.Id, stranger.Id);
        });
    }
}
