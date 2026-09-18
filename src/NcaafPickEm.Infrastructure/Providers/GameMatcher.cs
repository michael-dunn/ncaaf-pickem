using System.Globalization;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers;

/// <summary>
/// Ties a live score update to a <c>Games</c> row (04-Domain-Algorithms.md section 9). Built once
/// per apply run over the candidate games for the day, then asked about each update.
/// </summary>
/// <remarks>
/// Order: the stored provider id first (every poll after the first), then the normalized team
/// pair, then the same pair with the sides swapped, because ESPN's home/away labelling at a
/// neutral site need not agree with CFBD's. Everything the matcher learns - the event id and both
/// team ids - is persisted by the caller so the id path serves the next poll.
/// The date is implicit: the caller decides which games are candidates.
/// </remarks>
public sealed class GameMatcher
{
    private readonly TeamNameIndex _index;
    private readonly IReadOnlyList<Game> _candidates;
    private readonly Dictionary<long, Game> _byEspnEventId;
    private readonly Dictionary<long, Game> _byCfbdGameId;

    /// <summary>Creates a matcher over one day's candidate games.</summary>
    /// <param name="index">Name index built from every team and the provider's aliases.</param>
    /// <param name="candidates">The games an update could be about.</param>
    public GameMatcher(TeamNameIndex index, IReadOnlyList<Game> candidates)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(candidates);

        _index = index;
        _candidates = candidates;

        _byEspnEventId = candidates
            .Where(game => game.EspnEventId is not null)
            .GroupBy(game => game.EspnEventId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        _byCfbdGameId = candidates
            .GroupBy(game => game.CfbdGameId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>Matches one update.</summary>
    /// <param name="update">The provider's update.</param>
    public GameMatchResult Match(LiveScoreUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return update.Source == ProviderSource.Cfbd ? MatchCfbd(update) : MatchEspn(update);
    }

    private GameMatchResult MatchCfbd(LiveScoreUpdate update)
    {
        // The CFBD fallback carries no school names at all; its event id is the CFBD game id,
        // which is the natural key of the schedule we ingested from the same source.
        if (TryParseId(update.SourceEventId, out long cfbdGameId)
            && _byCfbdGameId.TryGetValue(cfbdGameId, out Game? game))
        {
            return new GameMatchResult(GameMatchOutcome.MatchedById, game, false, "CfbdGameId");
        }

        return new GameMatchResult(
            GameMatchOutcome.Ignored,
            null,
            false,
            $"CFBD game {update.SourceEventId} is not in this window's schedule");
    }

    private GameMatchResult MatchEspn(LiveScoreUpdate update)
    {
        if (TryParseId(update.SourceEventId, out long espnEventId)
            && _byEspnEventId.TryGetValue(espnEventId, out Game? known))
        {
            return new GameMatchResult(GameMatchOutcome.MatchedById, known, false, "EspnEventId");
        }

        Team? home = _index.Resolve(update.HomeName);
        Team? away = _index.Resolve(update.AwayName);

        // Last resort, and only for a side the names could not place at all.
        home ??= _index.ResolveAbbreviation(update.HomeAbbreviation);
        away ??= _index.ResolveAbbreviation(update.AwayAbbreviation);

        // Matching comes before any FBS filtering: an FCS opponent that is genuinely on our
        // schedule (CFBD ingests those games too) still deserves its live score. Classification
        // only decides what to do with an event we could *not* place.
        if (home is not null && away is not null)
        {
            Game[] straight = [.. _candidates.Where(g => g.HomeTeamId == home.Id && g.AwayTeamId == away.Id)];
            Game[] swapped = [.. _candidates.Where(g => g.HomeTeamId == away.Id && g.AwayTeamId == home.Id)];

            if (straight.Length + swapped.Length > 1)
            {
                return new GameMatchResult(
                    GameMatchOutcome.Ambiguous,
                    null,
                    false,
                    "more than one game in the window has this team pair");
            }

            if (straight.Length == 1)
            {
                return new GameMatchResult(GameMatchOutcome.MatchedByTeams, straight[0], false, "team names");
            }

            if (swapped.Length == 1)
            {
                return new GameMatchResult(GameMatchOutcome.MatchedByTeams, swapped[0], true, "team names, sides swapped");
            }
        }

        return Unplaceable(home, away);
    }

    /// <summary>
    /// Decides whether an event we could not place is a data problem worth a commissioner's time
    /// or just payload noise. <c>groups=80</c> is not an FBS filter (D-012): a Saturday payload is
    /// full of FCS opponents and FCS-vs-FCS games that we deliberately do not hold.
    /// </summary>
    private static GameMatchResult Unplaceable(Team? home, Team? away)
    {
        if (home is null && away is null)
        {
            return new GameMatchResult(
                GameMatchOutcome.Ignored,
                null,
                false,
                "neither team is a known school");
        }

        if (home is { Classification: not TeamClassification.Fbs }
            || away is { Classification: not TeamClassification.Fbs })
        {
            return new GameMatchResult(
                GameMatchOutcome.Ignored,
                null,
                false,
                "an FCS or lower team is playing");
        }

        return new GameMatchResult(
            GameMatchOutcome.Unmatched,
            null,
            false,
            home is null || away is null
                ? "only one side resolved to a known FBS team"
                : "both teams are known but no scheduled game pairs them");
    }

    private static bool TryParseId(string? value, out long id) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
}
