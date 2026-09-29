using Microsoft.EntityFrameworkCore;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Shared.Enums;
using NcaafPickEm.Simulator.Catalog;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Plays CFBD's teams-and-conferences feed: writes the catalog into <c>Conferences</c> and
/// <c>Teams</c>, upserting by CFBD id as the real ingest does, so running it twice changes nothing.
/// </summary>
public sealed class CatalogDesk
{
    private readonly AppDbContext _database;

    /// <summary>Creates the desk.</summary>
    public CatalogDesk(AppDbContext database)
    {
        _database = database;
    }

    /// <summary>Upserts every catalog conference and team; returns how many teams were new.</summary>
    public async Task<int> EnsureCatalogAsync(TeamCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        Dictionary<int, Conference> conferences = await _database.Conferences
            .ToDictionaryAsync(conference => conference.CfbdId, cancellationToken)
            .ConfigureAwait(false);

        foreach (CatalogConference entry in catalog.Conferences)
        {
            if (!conferences.TryGetValue(entry.CfbdId, out Conference? conference))
            {
                conference = new Conference { Id = Guid.CreateVersion7(), CfbdId = entry.CfbdId };
                _database.Conferences.Add(conference);
                conferences[entry.CfbdId] = conference;
            }

            conference.Name = entry.Name;
            conference.Abbreviation = entry.Abbreviation;
            conference.Classification = TeamClassification.Fbs;
        }

        Dictionary<string, Guid> conferenceIds = catalog.Conferences.ToDictionary(
            entry => entry.Abbreviation,
            entry => conferences[entry.CfbdId].Id,
            StringComparer.Ordinal);

        Dictionary<int, Team> teams = await _database.Teams
            .ToDictionaryAsync(team => team.CfbdId, cancellationToken)
            .ConfigureAwait(false);

        int added = 0;
        foreach (CatalogTeam entry in catalog.Teams)
        {
            if (!teams.TryGetValue(entry.Id, out Team? team))
            {
                team = new Team { Id = Guid.CreateVersion7(), CfbdId = entry.Id };
                _database.Teams.Add(team);
                added++;
            }

            team.School = entry.School;
            team.Mascot = entry.Mascot;
            team.Abbreviation = entry.Abbreviation;
            team.Classification = entry.Fcs ? TeamClassification.Fcs : TeamClassification.Fbs;
            team.ConferenceId = entry.Conference is string abbreviation && conferenceIds.TryGetValue(abbreviation, out Guid id)
                ? id
                : null;
            team.LogoUrl = TeamCatalog.LogoUrl(entry.Id);
        }

        await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return added;
    }
}
