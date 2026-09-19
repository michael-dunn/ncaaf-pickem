using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Infrastructure.Seeding;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// Loads the Week 7, 2026 fixture schedule/teams/rankings/lines into the shared test database
/// (P3-03 needs real games; <see cref="FixtureSeeder"/> is already idempotent, guarded on
/// <c>Teams.Any()</c>, so calling this from several test classes in the same run is safe), plus
/// lookups by the fixture's CFBD ids so tests can name games and teams the way
/// <c>Data/Week7_2026/schedule.json</c>/<c>teams.json</c> do.
/// </summary>
public static class FixtureGameData
{
    /// <summary>The fixture season and week every Data/Week7_2026 file is for.</summary>
    public const int SeasonYear = 2026;

    /// <summary>The fixture season and week every Data/Week7_2026 file is for.</summary>
    public const int Week = 7;

    /// <summary>Seeds reference data (no demo league) if it is not already present.</summary>
    public static async Task EnsureSeededAsync(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using AsyncServiceScope scope = factory.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        FixtureSeeder seeder = scope.ServiceProvider.GetRequiredService<FixtureSeeder>();
        await seeder.SeedAsync(seedDemoLeague: false);
    }

    /// <summary>The <c>Games.Id</c> for a fixture <c>cfbdGameId</c> (e.g. 700001 = Michigan/Texas).</summary>
    public static async Task<Guid> GetGameIdAsync(ApiFactory factory, long cfbdGameId) =>
        await factory.QueryDbAsync(database => database.Games
            .Where(g => g.CfbdGameId == cfbdGameId)
            .Select(g => g.Id)
            .FirstAsync());

    /// <summary>The <c>Teams.Id</c> for a fixture <c>cfbdTeamId</c> (e.g. 900101 = Michigan).</summary>
    public static async Task<Guid> GetTeamIdAsync(ApiFactory factory, int cfbdTeamId) =>
        await factory.QueryDbAsync(database => database.Teams
            .Where(t => t.CfbdId == cfbdTeamId)
            .Select(t => t.Id)
            .FirstAsync());

    /// <summary>The <c>Conferences.Id</c> for a fixture <c>cfbdConferenceId</c> (e.g. 8 = SEC).</summary>
    public static async Task<Guid> GetConferenceIdAsync(ApiFactory factory, int cfbdConferenceId) =>
        await factory.QueryDbAsync(database => database.Conferences
            .Where(c => c.CfbdId == cfbdConferenceId)
            .Select(c => c.Id)
            .FirstAsync());
}
