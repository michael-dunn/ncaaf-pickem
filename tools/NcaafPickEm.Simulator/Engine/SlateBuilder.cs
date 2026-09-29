namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Builds a week's slate that exercises the app's rules (P11-01): ranked teams for the Top 25
/// rule, same-conference matchups for conference rules and the conference-game point rule, close
/// lines for the close-spread point rule, a few unranked games for the commissioner to add by
/// hand, one game with no line, and - on request - an FCS opponent and an off-day game that set
/// generation has to leave out.
/// </summary>
/// <remarks>
/// Pure and seeded, so a test (or a tester who reruns a week) gets the same slate from the same
/// inputs. Kickoffs are spread round-robin over windows of <see cref="SlateOptions.GamesPerSlot"/>,
/// best-ranked games first, so the first window always holds a ranked game and "the first
/// kickoff" is the lock of any league with a Top 25 rule.
/// </remarks>
public static class SlateBuilder
{
    /// <summary>Games whose line is inside this are "close" for the default point rule.</summary>
    public const decimal CloseSpread = 3.5m;

    private const int UnrankedOnlyGames = 3;

    /// <summary>Builds the slate.</summary>
    /// <param name="teams">Every team that may play; teams already playing this week excluded by the caller.</param>
    /// <param name="ranks">This week's AP rank per team id.</param>
    /// <param name="options">Shape of the slate.</param>
    /// <param name="firstKickoffUtc">Kickoff of the first window: the lock, for a Top 25 league.</param>
    /// <param name="seed">Varies the matchups; the week number is a good one.</param>
    public static IReadOnlyList<SlateGameSpec> Build(
        IReadOnlyList<SlateTeam> teams,
        IReadOnlyDictionary<Guid, int> ranks,
        SlateOptions options,
        DateTimeOffset firstKickoffUtc,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(ranks);
        ArgumentNullException.ThrowIfNull(options);

        SlateOptions shape = options.Normalized();
        var random = new Random(seed);

        List<SlateTeam> ranked =
        [
            .. teams
                .Where(team => team.IsFbs && ranks.ContainsKey(team.TeamId))
                .OrderBy(team => ranks[team.TeamId]),
        ];
        List<SlateTeam> unranked = Shuffle([.. teams.Where(team => team.IsFbs && !ranks.ContainsKey(team.TeamId))], random);
        List<SlateTeam> fcs = Shuffle([.. teams.Where(team => !team.IsFbs)], random);

        int specials = (shape.IncludeFcsGame && fcs.Count > 0 ? 1 : 0) + (shape.IncludeOffDayGame ? 1 : 0);
        int regular = Math.Max(1, shape.GameCount - specials);
        int unrankedOnly = Math.Min(UnrankedOnlyGames, Math.Max(0, regular - 1));

        var pairs = new List<(SlateTeam Home, SlateTeam Away, bool IsSaturday)>();

        // Ranked teams, best first, against a ranked conference rival now and then and otherwise
        // an unranked team - from their own conference half the time.
        while (pairs.Count < regular - unrankedOnly && ranked.Count > 0)
        {
            SlateTeam team = Take(ranked, 0);

            int rival = ranked.FindIndex(candidate => candidate.SharesConferenceWith(team));
            SlateTeam? opponent = rival >= 0 && random.NextDouble() < 0.35 ? Take(ranked, rival) : null;
            opponent ??= TakeOpponent(unranked, team, random.NextDouble() < 0.5)
                ?? (ranked.Count > 0 ? Take(ranked, ranked.Count - 1) : null);

            if (opponent is null)
            {
                break;
            }

            pairs.Add(random.NextDouble() < 0.6 ? (team, opponent, true) : (opponent, team, true));
        }

        // A few unranked-only games: nothing a Top 25 rule picks up, so there is something to add
        // by hand, and the first ones are conference games for a conference rule to find.
        while (pairs.Count < regular && unranked.Count >= 2)
        {
            SlateTeam home = Take(unranked, 0);
            SlateTeam? away = TakeOpponent(unranked, home, preferConference: pairs.Count % 2 == 0);
            if (away is null)
            {
                break;
            }

            pairs.Add((home, away, true));
        }

        if (shape.IncludeFcsGame && fcs.Count > 0 && unranked.Count > 0)
        {
            pairs.Add((Take(unranked, 0), fcs[0], true));
        }

        List<SlateGameSpec> games = Schedule(pairs, shape, firstKickoffUtc, ranks, random);

        if (shape.IncludeOffDayGame && unranked.Count >= 2)
        {
            // "Friday night": the window before the first one, stored as not-Saturday.
            SlateTeam home = Take(unranked, 0);
            SlateTeam away = Take(unranked, 0);
            games.Insert(0, new SlateGameSpec(
                home.TeamId,
                away.TeamId,
                firstKickoffUtc - TimeSpan.FromMinutes(shape.SlotMinutes),
                Spread(home, away, ranks, random),
                home.SharesConferenceWith(away),
                IsSaturday: false));
        }

        return EnsureCloseGames(games);
    }

    /// <summary>A home spread from rank and conference strength (negative = home favoured).</summary>
    internal static decimal? Spread(SlateTeam home, SlateTeam away, IReadOnlyDictionary<Guid, int> ranks, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (random.NextDouble() < 0.08)
        {
            return null;
        }

        double edge = Strength(home, ranks, random) - Strength(away, ranks, random) + 2.5;
        double points = Math.Clamp(-edge * 0.8, -35, 35);
        return Math.Round((decimal)points * 2, MidpointRounding.AwayFromZero) / 2;
    }

    private static List<SlateGameSpec> Schedule(
        List<(SlateTeam Home, SlateTeam Away, bool IsSaturday)> pairs,
        SlateOptions shape,
        DateTimeOffset firstKickoffUtc,
        IReadOnlyDictionary<Guid, int> ranks,
        Random random)
    {
        int slots = Math.Max(1, (int)Math.Ceiling(pairs.Count / (double)shape.GamesPerSlot));
        var games = new List<SlateGameSpec>(pairs.Count + 1);

        for (int index = 0; index < pairs.Count; index++)
        {
            (SlateTeam home, SlateTeam away, bool isSaturday) = pairs[index];
            DateTimeOffset kickoff = firstKickoffUtc + TimeSpan.FromMinutes(shape.SlotMinutes * (index % slots));

            games.Add(new SlateGameSpec(
                home.TeamId,
                away.TeamId,
                kickoff,
                Spread(home, away, ranks, random),
                home.SharesConferenceWith(away),
                isSaturday));
        }

        return [.. games.OrderBy(game => game.KickoffUtc)];
    }

    /// <summary>At least two lines inside <see cref="CloseSpread"/>, so the close-spread rule has work.</summary>
    private static List<SlateGameSpec> EnsureCloseGames(List<SlateGameSpec> games)
    {
        int close = games.Count(game => game.IsSaturday && game.Spread is decimal line && Math.Abs(line) < CloseSpread);
        if (close >= 2)
        {
            return games;
        }

        decimal[] replacements = [-2.5m, 1.5m, -3m];
        int replaced = 0;
        foreach (SlateGameSpec game in games
            .Where(game => game.IsSaturday && game.Spread is not null)
            .Where(game => Math.Abs(game.Spread!.Value) >= CloseSpread)
            .OrderBy(game => Math.Abs(game.Spread!.Value))
            .Take(2 - close)
            .ToList())
        {
            int index = games.IndexOf(game);
            games[index] = game with { Spread = replacements[replaced++ % replacements.Length] };
        }

        return games;
    }

    private static double Strength(SlateTeam team, IReadOnlyDictionary<Guid, int> ranks, Random random)
    {
        if (!team.IsFbs)
        {
            return 20;
        }

        if (ranks.TryGetValue(team.TeamId, out int rank))
        {
            return 80 - (rank * 0.8);
        }

        bool powerConference = team.Conference is "SEC" or "B1G" or "ACC" or "B12";
        return 42 + (powerConference ? 8 : 0) + (random.NextDouble() * 18);
    }

    private static SlateTeam? TakeOpponent(List<SlateTeam> pool, SlateTeam team, bool preferConference)
    {
        if (pool.Count == 0)
        {
            return null;
        }

        int index = preferConference ? pool.FindIndex(candidate => candidate.SharesConferenceWith(team)) : -1;
        return Take(pool, index >= 0 ? index : 0);
    }

    private static SlateTeam Take(List<SlateTeam> pool, int index)
    {
        SlateTeam team = pool[index];
        pool.RemoveAt(index);
        return team;
    }

    private static List<SlateTeam> Shuffle(List<SlateTeam> teams, Random random)
    {
        for (int index = teams.Count - 1; index > 0; index--)
        {
            int swap = random.Next(index + 1);
            (teams[index], teams[swap]) = (teams[swap], teams[index]);
        }

        return teams;
    }
}
