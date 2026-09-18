using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Fixtures;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Seeds the hand-verified ESPN spellings from the provider spike
/// (<c>tests/NcaafPickEm.Fixtures/Real/team-aliases-draft.json</c>) into <c>TeamAliases</c>.
/// </summary>
/// <remarks>
/// Call it from the teams refresh (P2-04), after the teams themselves are ingested: rows resolve
/// by <c>Teams.School</c>, so a school that is not in the table yet is skipped rather than
/// invented. Idempotent, and it never overwrites an alias that already exists - a commissioner
/// resolving an unmatched game wins over this file. Only rows marked <c>verified</c> are used;
/// the unverified FCS row is deliberately left out, since FCS games are ignored anyway (D-012).
/// CFBD's own <c>alternateNames</c> are a separate and much larger source that P2-02's ingest
/// seeds as <see cref="ProviderSource.Cfbd"/> aliases.
/// </remarks>
public static class TeamAliasSeed
{
    /// <summary>The capture this seed reads.</summary>
    public const string FixtureName = "team-aliases-draft.json";

    /// <summary>Inserts every verified alias that is missing. Returns how many rows were added.</summary>
    /// <param name="database">The database.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<int> SeedAsync(
        AppDbContext database,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);

        AliasRow[] rows = FixtureLoader.ReadReal<AliasRow[]>(FixtureName);

        List<Team> teams = await database.Teams.ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, Team> teamsBySchool = [];
        foreach (Team team in teams)
        {
            teamsBySchool.TryAdd(TeamNameNormalizer.Normalize(team.School), team);
        }

        List<TeamAlias> existing = await database.TeamAliases.ToListAsync(cancellationToken).ConfigureAwait(false);
        HashSet<(ProviderSource Source, string Alias)> known =
            [.. existing.Select(alias => (alias.Source, alias.Alias))];

        int added = 0;
        foreach (AliasRow row in rows)
        {
            if (!row.Verified
                || string.IsNullOrWhiteSpace(row.Alias)
                || !Enum.TryParse(row.Source, ignoreCase: true, out ProviderSource source))
            {
                continue;
            }

            if (!teamsBySchool.TryGetValue(TeamNameNormalizer.Normalize(row.School), out Team? team))
            {
                logger.LogInformation(
                    "Skipping alias {Alias}: no team named {School} is ingested yet",
                    row.Alias,
                    row.School);
                continue;
            }

            if (!known.Add((source, row.Alias)))
            {
                continue;
            }

            database.TeamAliases.Add(new TeamAlias
            {
                Id = Guid.CreateVersion7(),
                TeamId = team.Id,
                Source = source,
                Alias = row.Alias,
            });
            added++;
        }

        if (added > 0)
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded {AliasCount} provider team aliases", added);
        }

        return added;
    }

    private sealed record AliasRow(string Source, string Alias, string School, string? Note, bool Verified);
}
