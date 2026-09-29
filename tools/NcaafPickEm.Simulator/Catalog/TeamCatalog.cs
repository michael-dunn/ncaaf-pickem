using System.Globalization;
using System.Text.Json;

namespace NcaafPickEm.Simulator.Catalog;

/// <summary>
/// The simulator's embedded team catalog (<c>Catalog/teams.json</c>): every FBS program in its 2026
/// conference, a handful of FCS teams for the FBS-only rule, and a preseason Top 25 that seeds each
/// simulated week's AP poll.
/// </summary>
public sealed class TeamCatalog
{
    private const string ResourceName = "NcaafPickEm.Simulator.Catalog.teams.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<TeamCatalog> Embedded = new(Load);

    private TeamCatalog(
        IReadOnlyList<CatalogConference> conferences,
        IReadOnlyList<CatalogTeam> teams,
        IReadOnlyList<int> preseasonTop25)
    {
        Conferences = conferences;
        Teams = teams;
        PreseasonTop25 = preseasonTop25;
    }

    /// <summary>The catalog shipped with the simulator.</summary>
    public static TeamCatalog Default => Embedded.Value;

    /// <summary>The FBS conferences, keyed by their CFBD id.</summary>
    public IReadOnlyList<CatalogConference> Conferences { get; }

    /// <summary>Every team, FBS and FCS.</summary>
    public IReadOnlyList<CatalogTeam> Teams { get; }

    /// <summary>Team ids (CFBD) in preseason poll order, rank 1 first.</summary>
    public IReadOnlyList<int> PreseasonTop25 { get; }

    /// <summary>The logo URL the app renders, from the same CDN the CFBD ingest uses.</summary>
    /// <param name="cfbdTeamId">The team's CFBD id.</param>
    public static string LogoUrl(int cfbdTeamId) =>
        string.Create(CultureInfo.InvariantCulture, $"https://cdn.collegefootballdata.com/logos/500/{cfbdTeamId}.png");

    private static TeamCatalog Load()
    {
        using Stream stream = typeof(TeamCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The team catalog resource '{ResourceName}' is not embedded.");

        CatalogFile file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The team catalog deserialized to null.");

        return new TeamCatalog(file.Conferences, file.Teams, file.PreseasonTop25);
    }

    private sealed record CatalogFile(
        IReadOnlyList<CatalogConference> Conferences,
        IReadOnlyList<CatalogTeam> Teams,
        IReadOnlyList<int> PreseasonTop25);
}
