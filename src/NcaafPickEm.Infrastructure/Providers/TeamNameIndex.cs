using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Looks a provider's team name up against our <c>Teams</c> table: normalized <c>School</c>
/// first, then <c>TeamAliases</c> for that provider, then - only as a last resort -
/// <c>Abbreviation</c>.
/// </summary>
/// <remarks>
/// Abbreviations are deliberately a separate, explicit call rather than another key in the name
/// map. ESPN's abbreviations are its own (<c>USA</c>, <c>USM</c>, <c>USF</c>, <c>TA&amp;M</c>)
/// and collide across schools; a wrong abbreviation match silently scores the wrong game, so a
/// key that resolves to more than one team resolves to none at all (D-012).
/// </remarks>
public sealed class TeamNameIndex
{
    private readonly Dictionary<string, Team> _byName;
    private readonly Dictionary<string, Team> _byAbbreviation;

    private TeamNameIndex(Dictionary<string, Team> byName, Dictionary<string, Team> byAbbreviation)
    {
        _byName = byName;
        _byAbbreviation = byAbbreviation;
    }

    /// <summary>Builds an index for one provider's spellings.</summary>
    /// <param name="teams">Every known team.</param>
    /// <param name="aliases">Every known alias; only <paramref name="source"/>'s are used.</param>
    /// <param name="source">The provider whose aliases apply.</param>
    public static TeamNameIndex Build(
        IEnumerable<Team> teams,
        IEnumerable<TeamAlias> aliases,
        ProviderSource source)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(aliases);

        Dictionary<string, Team> byName = [];
        HashSet<string> ambiguousNames = [];
        Dictionary<string, Team> byAbbreviation = [];
        HashSet<string> ambiguousAbbreviations = [];

        Team[] allTeams = [.. teams];
        Dictionary<Guid, Team> teamsById = allTeams.ToDictionary(team => team.Id);

        foreach (Team team in allTeams)
        {
            Add(byName, ambiguousNames, TeamNameNormalizer.Normalize(team.School), team);
            Add(byAbbreviation, ambiguousAbbreviations, NormalizeAbbreviation(team.Abbreviation), team);
        }

        foreach (TeamAlias alias in aliases.Where(a => a.Source == source))
        {
            if (teamsById.TryGetValue(alias.TeamId, out Team? team))
            {
                Add(byName, ambiguousNames, TeamNameNormalizer.Normalize(alias.Alias), team);
            }
        }

        return new TeamNameIndex(byName, byAbbreviation);
    }

    /// <summary>
    /// The team whose school name or alias matches, or <see langword="null"/> when nothing
    /// matches or the key is ambiguous.
    /// </summary>
    /// <param name="name">The provider's school name.</param>
    public Team? Resolve(string? name)
    {
        string key = TeamNameNormalizer.Normalize(name);
        return key.Length > 0 && _byName.TryGetValue(key, out Team? team) ? team : null;
    }

    /// <summary>
    /// The team whose abbreviation matches exactly, case-insensitively. Last resort only.
    /// </summary>
    /// <param name="abbreviation">The provider's abbreviation.</param>
    public Team? ResolveAbbreviation(string? abbreviation)
    {
        string key = NormalizeAbbreviation(abbreviation);
        return key.Length > 0 && _byAbbreviation.TryGetValue(key, out Team? team) ? team : null;
    }

    private static string NormalizeAbbreviation(string? abbreviation) =>
        string.IsNullOrWhiteSpace(abbreviation) ? string.Empty : abbreviation.Trim().ToUpperInvariant();

    private static void Add(Dictionary<string, Team> map, HashSet<string> ambiguous, string key, Team team)
    {
        if (key.Length == 0 || ambiguous.Contains(key))
        {
            return;
        }

        if (map.TryGetValue(key, out Team? existing))
        {
            if (existing.Id == team.Id)
            {
                return;
            }

            // Two schools share the key. Resolving it either way would be a coin flip, so it
            // resolves to nothing and the game surfaces as unmatched instead.
            map.Remove(key);
            ambiguous.Add(key);
            return;
        }

        map[key] = team;
    }
}
