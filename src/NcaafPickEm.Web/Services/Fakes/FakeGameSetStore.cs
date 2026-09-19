using Microsoft.AspNetCore.Components;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Points;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// Shared in-memory state behind <see cref="FakeGameSetsApi"/> and <see cref="FakePointRulesApi"/>,
/// so a point override made through one shows up through the other without a real backend.
/// Seeded from the "Week 7, 2026" fixture (<c>tests/NcaafPickEm.Fixtures/Data/Week7_2026</c>): the
/// same teams, conferences, kickoffs, and AP ranks, reduced to the FBS-vs-FBS Saturday games (the
/// 2 FCS games and the 1 Friday game are excluded, matching what the real candidate search would
/// return). Registered scoped; Blazor WebAssembly has one DI scope per session, so state persists
/// for the lifetime of the app exactly like the real API would.
/// </summary>
/// <remarks>
/// Visiting the app with <c>?locked=1</c> on the initial URL marks week 7's game set locked, for
/// screenshotting the locked state of "This week's games" (orchestrator guidance for P3-05).
/// </remarks>
public sealed class FakeGameSetStore
{
    /// <summary>The one season/week this store knows about, matching <see cref="FakeLeaguesApi"/>'s sample league.</summary>
    public const int SeasonYear = 2026;

    /// <summary>Current week of the sample league.</summary>
    public const int Week = 7;

    private const int DefaultPointValue = 10;

    private readonly List<FakeGame> _games;
    private readonly Dictionary<int, WeekRulesResponse> _weekOverrides = [];
    private readonly bool _locked;

    /// <summary>Conferences referenced by the seeded teams.</summary>
    public ConferenceDto[] Conferences { get; }

    /// <summary>Teams referenced by the seeded games.</summary>
    public TeamDto[] Teams { get; }

    /// <summary>The league's default game set rules.</summary>
    public List<GameSetRuleDto> DefaultRules { get; } = [new(RuleId: Guid.NewGuid(), GameSetRuleType.Top25, null, null, null, null, false, 0)];

    /// <summary>The league's point rules, ordered by <see cref="PointRuleDto.Priority"/>.</summary>
    public List<PointRuleDto> PointRules { get; } = [];

    /// <summary>Reads the <c>?locked=1</c> query flag once at construction.</summary>
    /// <param name="navigation">Used only to read the query flag at startup.</param>
    public FakeGameSetStore(NavigationManager navigation)
    {
        _locked = navigation.Uri.Contains("locked=1", StringComparison.OrdinalIgnoreCase);

        (Conferences, Teams) = SeedReferenceData();
        _games = SeedGames(Teams);
    }

    /// <summary>True when week 7's game set is locked (the <c>?locked=1</c> flag).</summary>
    public bool IsLocked => _locked;

    /// <summary>Games currently in the week's set, ordered by kickoff.</summary>
    public IReadOnlyList<FakeGame> Games => _games.OrderBy(g => g.KickoffUtc).ToArray();

    /// <summary>All Saturday FBS candidates for the week, including ones not currently in the set.</summary>
    public IReadOnlyList<FakeGame> AllCandidates { get; private set; } = [];

    /// <summary>Gets (creating a default) the override state for a given week.</summary>
    public WeekRulesResponse GetWeekRules(int week) =>
        _weekOverrides.TryGetValue(week, out WeekRulesResponse? value)
            ? value
            : new WeekRulesResponse(UsesOverride: false, Rules: [.. DefaultRules]);

    /// <summary>Sets the override state for a given week.</summary>
    public void SetWeekRules(int week, WeekRulesResponse rules) => _weekOverrides[week] = rules;

    /// <summary>Removes a game from the current set (manual remove).</summary>
    public void RemoveGame(Guid gameId) => _games.RemoveAll(g => g.GameId == gameId);

    /// <summary>Adds a candidate game to the current set (manual add), if not already present.</summary>
    public void AddGame(Guid gameId)
    {
        if (_games.Any(g => g.GameId == gameId))
        {
            return;
        }

        FakeGame? candidate = AllCandidates.FirstOrDefault(g => g.GameId == gameId);
        if (candidate is null)
        {
            throw new LeaguesApiException(404, "Game not found.");
        }

        candidate.Source = GameSetGameSource.Manual;
        candidate.PointValue = ResolvePointValue(candidate);
        _games.Add(candidate);
    }

    /// <summary>Replaces the current set with whatever the given rules would select.</summary>
    public void Generate(IReadOnlyList<GameSetRuleDto> rules)
    {
        _games.Clear();
        foreach (FakeGame candidate in Select(rules))
        {
            candidate.Source = GameSetGameSource.Rule;
            candidate.PointValue = ResolvePointValue(candidate);
            _games.Add(candidate);
        }
    }

    /// <summary>Previews what the given (possibly unsaved) rules would select, without mutating the set.</summary>
    public GameSetPreview Preview(IReadOnlyList<GameSetRuleDto> rules)
    {
        FakeGame[] selected = Select(rules).OrderBy(g => g.KickoffUtc).ToArray();
        GameSetGameDto[] games = [.. selected.Select(g => g.ToDto(ResolvePointValue(g)))];
        return new GameSetPreview(games, games.Length, ExceedsMax: games.Length > 50, UsedFallbackRankings: false);
    }

    /// <summary>Sets or clears a manual point override on a game currently in the set.</summary>
    public void SetOverride(Guid gameId, int? pointValue)
    {
        FakeGame game = _games.FirstOrDefault(g => g.GameId == gameId)
            ?? throw new LeaguesApiException(404, "Game not found.");

        if (pointValue is null)
        {
            game.ManualOverride = null;
            game.PointValue = ResolvePointValue(game);
        }
        else
        {
            game.ManualOverride = pointValue;
            game.PointValue = pointValue.Value;
        }
    }

    private int ResolvePointValue(FakeGame game)
    {
        if (game.ManualOverride is { } manual)
        {
            return manual;
        }

        foreach (PointRuleDto rule in PointRules.OrderBy(r => r.Priority))
        {
            bool matches = rule.RuleType switch
            {
                PointRuleType.ConferenceGame when rule.ConferenceId is { } confId =>
                    game.Home.ConferenceId == confId && game.Away.ConferenceId == confId,
                PointRuleType.ConferenceGame => game.Home.ConferenceId == game.Away.ConferenceId && game.Home.ConferenceId is not null,
                PointRuleType.Team => game.Home.TeamId == rule.TeamId || game.Away.TeamId == rule.TeamId,
                PointRuleType.CloseSpread => false, // Fake has no spreads; never matches.
                _ => false,
            };

            if (matches)
            {
                return rule.PointValue;
            }
        }

        return DefaultPointValue;
    }

    private IEnumerable<FakeGame> Select(IReadOnlyList<GameSetRuleDto> rules)
    {
        var seen = new HashSet<Guid>();
        foreach (FakeGame candidate in AllCandidates)
        {
            bool matches = rules.Any(rule => rule.RuleType switch
            {
                GameSetRuleType.Top25 => candidate.HomeRank is not null || candidate.AwayRank is not null,
                GameSetRuleType.Conference => rule.ConferenceGamesOnly
                    ? candidate.Home.ConferenceId == rule.ConferenceId && candidate.Away.ConferenceId == rule.ConferenceId
                    : candidate.Home.ConferenceId == rule.ConferenceId || candidate.Away.ConferenceId == rule.ConferenceId,
                GameSetRuleType.Team => candidate.Home.TeamId == rule.TeamId || candidate.Away.TeamId == rule.TeamId,
                _ => false,
            });

            if (matches && seen.Add(candidate.GameId))
            {
                yield return candidate;
            }
        }
    }

    private static Guid ConfGuid(int n) => Guid.Parse($"00000000-0000-0000-0002-{n:x12}");

    private static Guid TeamGuid(int n) => Guid.Parse($"00000000-0000-0000-0001-{n:x12}");

    private (ConferenceDto[] Conferences, TeamDto[] Teams) SeedReferenceData()
    {
        var bigTen = new ConferenceDto(ConfGuid(5), "Big Ten Conference", "B1G");
        var sec = new ConferenceDto(ConfGuid(8), "Southeastern Conference", "SEC");
        var acc = new ConferenceDto(ConfGuid(1), "Atlantic Coast Conference", "ACC");
        var big12 = new ConferenceDto(ConfGuid(4), "Big 12 Conference", "B12");
        var mwc = new ConferenceDto(ConfGuid(17), "Mountain West Conference", "MWC");

        ConferenceDto[] conferences = [bigTen, sec, acc, big12, mwc];

        TeamDto Team(int n, string school, string abbr, ConferenceDto? conf) =>
            new(TeamGuid(n), school, abbr, conf?.ConferenceId, LogoUrl: null);

        TeamDto[] teams =
        [
            Team(101, "Michigan", "MICH", bigTen),
            Team(102, "Texas", "TEX", sec),
            Team(103, "Maryland", "MD", bigTen),
            Team(104, "Rutgers", "RUTG", bigTen),
            Team(105, "Ohio State", "OSU", bigTen),
            Team(106, "Wisconsin", "WIS", bigTen),
            Team(107, "Alabama", "ALA", sec),
            Team(108, "Auburn", "AUB", sec),
            Team(109, "Georgia", "UGA", sec),
            Team(110, "Kentucky", "UK", sec),
            Team(111, "Oklahoma", "OU", sec),
            Team(112, "Missouri", "MIZ", sec),
            Team(113, "Clemson", "CLEM", acc),
            Team(114, "Florida State", "FSU", acc),
            Team(117, "Iowa State", "ISU", big12),
            Team(118, "Kansas", "KU", big12),
            Team(119, "TCU", "TCU", big12),
            Team(120, "Baylor", "BAY", big12),
            Team(121, "Penn State", "PSU", bigTen),
            Team(123, "Indiana", "IND", bigTen),
            Team(125, "Boise State", "BSU", mwc),
            Team(126, "Fresno State", "FRES", mwc),
        ];

        return (conferences, teams);
    }

    private List<FakeGame> SeedGames(TeamDto[] teams)
    {
        TeamDto Find(string abbr) => teams.First(t => t.Abbreviation == abbr);

        // Kickoffs and ranks match tests/NcaafPickEm.Fixtures/Data/Week7_2026 (schedule.json,
        // rankings.json): Michigan #8 vs Texas #5 (Top 25 match), Maryland/Rutgers is a Big Ten
        // conference game, etc. Reduced to Saturday FBS-vs-FBS games only.
        List<FakeGame> all =
        [
            NewGame(Find("MICH"), 8, Find("TEX"), 5, "2026-10-17T19:30:00Z"),
            NewGame(Find("MD"), null, Find("RUTG"), null, "2026-10-17T16:00:00Z"),
            NewGame(Find("OSU"), null, Find("WIS"), null, "2026-10-17T16:00:00Z"),
            NewGame(Find("ALA"), 11, Find("AUB"), null, "2026-10-17T19:30:00Z"),
            NewGame(Find("UGA"), 2, Find("UK"), null, "2026-10-17T16:00:00Z"),
            NewGame(Find("OU"), null, Find("MIZ"), null, "2026-10-17T23:00:00Z"),
            NewGame(Find("CLEM"), null, Find("FSU"), null, "2026-10-17T16:00:00Z"),
            NewGame(Find("ISU"), null, Find("KU"), null, "2026-10-17T20:00:00Z"),
            NewGame(Find("TCU"), null, Find("BAY"), null, "2026-10-17T19:00:00Z"),
            NewGame(Find("PSU"), null, Find("IND"), null, "2026-10-17T19:30:00Z"),
            NewGame(Find("BSU"), null, Find("FRES"), null, "2026-10-16T23:30:00Z"), // Fri Pacific -> Sat Eastern.
        ];

        AllCandidates = all;

        // The initial game set (as if already generated from the default Top-25 rule): only the
        // ranked matchup, unless the page has already regenerated in this session.
        return [.. all.Where(g => g.HomeRank is not null || g.AwayRank is not null)];
    }

    private static FakeGame NewGame(TeamDto home, int? homeRank, TeamDto away, int? awayRank, string kickoffUtc) =>
        new()
        {
            GameId = Guid.NewGuid(),
            Home = home,
            Away = away,
            HomeRank = homeRank,
            AwayRank = awayRank,
            KickoffUtc = DateTimeOffset.Parse(kickoffUtc),
            PointValue = DefaultPointValue,
            Source = GameSetGameSource.Rule,
        };

    /// <summary>Mutable in-memory shape of one game, converted to <see cref="GameSetGameDto"/> on the way out.</summary>
    public sealed class FakeGame
    {
        public required Guid GameId { get; set; }
        public required TeamDto Home { get; set; }
        public required TeamDto Away { get; set; }
        public int? HomeRank { get; set; }
        public int? AwayRank { get; set; }
        public required DateTimeOffset KickoffUtc { get; set; }
        public int PointValue { get; set; }
        public int? ManualOverride { get; set; }
        public GameSetGameSource Source { get; set; }

        public GameSetGameDto ToDto(int? pointValueOverride = null) => new(
            GameSetGameId: GameId,
            GameId: GameId,
            HomeTeam: Home,
            AwayTeam: Away,
            HomeRank: HomeRank,
            AwayRank: AwayRank,
            KickoffUtc: KickoffUtc,
            PointValue: pointValueOverride ?? PointValue,
            IsPointValueElevated: (pointValueOverride ?? PointValue) > DefaultPointValue,
            Source: Source,
            Status: GameStatus.Scheduled,
            HomeScore: null,
            AwayScore: null,
            Period: null,
            Clock: null,
            IsVoided: false,
            WinnerTeamId: null);
    }
}
