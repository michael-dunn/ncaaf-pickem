using Microsoft.AspNetCore.Components;
using NcaafPickEm.Shared.Contracts.Dashboard;
using NcaafPickEm.Shared.Contracts.GameSets;
using NcaafPickEm.Shared.Contracts.Reference;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// Development-only <see cref="IDashboardApi"/>, seeded with the Overview worked example
/// (Michael, Alyson, Dance, Alex, Daniel; Michigan/Texas and Maryland/Rutgers) plus two extra
/// games this task added to demonstrate the "everyone agrees" and "no pick, show both sides"
/// cases the worked example itself does not exercise. Gated the same way as every other client
/// fake (D-044): selected only when both <c>DEBUG</c> and <c>USE_FAKE_API</c> are defined.
/// </summary>
/// <remarks>
/// Two query flags, both read once at construction like <see cref="FakeGameSetStore"/>'s
/// <c>?locked=1</c>: <c>?viewer=alyson</c> (default is Dance, matching the worked example's own
/// "Dashboard for Dance" framing) picks whose dashboard is rendered, and
/// <c>?state=prelock|live|final|stale</c> (default <c>live</c>) picks which of the four
/// screenshot states to render. <c>stale</c> reuses the live game states but drops <c>Period</c>/
/// <c>Clock</c> on the in-progress game and sets <c>ScoresMayBeStale</c>, matching what the real
/// CFBD fallback provider cannot report (WorkItems/12-Data-Provider-Evaluation.txt).
/// </remarks>
public sealed class FakeDashboardApi : IDashboardApi
{
    private static readonly Guid MichaelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AlysonId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DanceId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid AlexId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid DanielId = Guid.Parse("00000000-0000-0000-0000-000000000005");

    private static readonly MemberRef Michael = new(MichaelId, "Michael", IsFormer: false);
    private static readonly MemberRef Alyson = new(AlysonId, "Alyson", IsFormer: false);
    private static readonly MemberRef Dance = new(DanceId, "Dance", IsFormer: false);
    private static readonly MemberRef Alex = new(AlexId, "Alex", IsFormer: false);

    // Daniel is rendered as a former member (removed since lock, active at lock) purely to give
    // the chip screenshots a "former member" marker to show - the worked example itself has no
    // such member.
    private static readonly MemberRef Daniel = new(DanielId, "Daniel", IsFormer: true);

    private readonly Guid _viewerId;
    private readonly string _state;

    /// <summary>Reads <c>?viewer=</c>/<c>?state=</c> once at construction.</summary>
    /// <param name="navigation">Used only to read the query flags at startup.</param>
    public FakeDashboardApi(NavigationManager navigation)
    {
        string uri = navigation.Uri;

        _viewerId = true switch
        {
            _ when uri.Contains("viewer=alyson", StringComparison.OrdinalIgnoreCase) => AlysonId,
            _ when uri.Contains("viewer=michael", StringComparison.OrdinalIgnoreCase) => MichaelId,
            _ when uri.Contains("viewer=alex", StringComparison.OrdinalIgnoreCase) => AlexId,
            _ when uri.Contains("viewer=daniel", StringComparison.OrdinalIgnoreCase) => DanielId,
            _ => DanceId,
        };

        _state = true switch
        {
            _ when uri.Contains("state=prelock", StringComparison.OrdinalIgnoreCase) => "prelock",
            _ when uri.Contains("state=final", StringComparison.OrdinalIgnoreCase) => "final",
            _ when uri.Contains("state=stale", StringComparison.OrdinalIgnoreCase) => "stale",
            _ => "live",
        };
    }

    /// <inheritdoc />
    public Task<DashboardResponse> GetDashboardAsync(
        Guid leagueId, int week, CancellationToken cancellationToken = default) =>
        Task.FromResult(Build());

    private DashboardResponse Build()
    {
        TeamDto michigan = Team("Michigan", "MICH");
        TeamDto texas = Team("Texas", "TEX");
        TeamDto maryland = Team("Maryland", "MD");
        TeamDto rutgers = Team("Rutgers", "RUTG");
        TeamDto alabama = Team("Alabama", "ALA");
        TeamDto auburn = Team("Auburn", "AUB");
        TeamDto georgia = Team("Georgia", "UGA");
        TeamDto kentucky = Team("Kentucky", "UK");

        DateTimeOffset kickoff1 = DateTimeOffset.Parse("2026-10-17T16:00:00Z");
        DateTimeOffset kickoff2 = DateTimeOffset.Parse("2026-10-17T19:30:00Z");
        DateTimeOffset kickoff3 = DateTimeOffset.Parse("2026-10-17T23:00:00Z");
        DateTimeOffset kickoff4 = DateTimeOffset.Parse("2026-10-17T20:00:00Z");

        if (_state == "prelock")
        {
            return new DashboardResponse(
                IsAvailable: false,
                LockAtUtc: kickoff1,
                LockAtEasternDisplay: "Sat 12:00 PM ET",
                PointsSoFar: 0,
                MaxRemaining: 0,
                ScoresMayBeStale: false,
                Games: [],
                EveryoneAgrees: []);
        }

        bool final = _state == "final";
        bool stale = _state == "stale";

        // Michigan (#8) vs Texas (#5): Michael/Alyson/Alex/Daniel pick Michigan, Dance picks
        // Texas - the worked example's own headline swing game.
        Game game1 = new(
            GameSetGameId: Guid.Parse("00000000-0000-0000-0003-000000000001"),
            Home: michigan,
            Away: texas,
            HomeRank: 8,
            AwayRank: 5,
            KickoffUtc: kickoff1,
            PointValue: 10,
            Status: final ? GameStatus.Final : GameStatus.InProgress,
            HomeScore: final ? 31 : 21,
            AwayScore: final ? 24 : 17,
            Period: stale || final ? null : (byte)3,
            Clock: stale || final ? null : "07:12",
            WinnerTeamId: final ? michigan.TeamId : null,
            Picks: new Dictionary<Guid, Guid>
            {
                [MichaelId] = michigan.TeamId,
                [AlysonId] = michigan.TeamId,
                [DanceId] = texas.TeamId,
                [AlexId] = michigan.TeamId,
                [DanielId] = michigan.TeamId,
            });

        // Maryland vs Rutgers: Michael/Alyson/Dance/Daniel pick Maryland, Alex picks Rutgers.
        // Already decided (Final) in every non-prelock state so the live/stale screenshots also
        // show a completed swing game alongside the in-progress one.
        Game game2 = new(
            GameSetGameId: Guid.Parse("00000000-0000-0000-0003-000000000002"),
            Home: maryland,
            Away: rutgers,
            HomeRank: null,
            AwayRank: null,
            KickoffUtc: kickoff2,
            PointValue: 10,
            Status: GameStatus.Final,
            HomeScore: 27,
            AwayScore: 20,
            Period: null,
            Clock: null,
            WinnerTeamId: maryland.TeamId,
            Picks: new Dictionary<Guid, Guid>
            {
                [MichaelId] = maryland.TeamId,
                [AlysonId] = maryland.TeamId,
                [DanceId] = maryland.TeamId,
                [AlexId] = rutgers.TeamId,
                [DanielId] = maryland.TeamId,
            });

        // Alabama vs Auburn: everyone picks Alabama - the "everyone agrees" example.
        Game game3 = new(
            GameSetGameId: Guid.Parse("00000000-0000-0000-0003-000000000003"),
            Home: alabama,
            Away: auburn,
            HomeRank: 11,
            AwayRank: null,
            KickoffUtc: kickoff3,
            PointValue: 10,
            Status: final ? GameStatus.Final : GameStatus.Scheduled,
            HomeScore: final ? 38 : null,
            AwayScore: final ? 14 : null,
            Period: null,
            Clock: null,
            WinnerTeamId: final ? alabama.TeamId : null,
            Picks: new Dictionary<Guid, Guid>
            {
                [MichaelId] = alabama.TeamId,
                [AlysonId] = alabama.TeamId,
                [DanceId] = alabama.TeamId,
                [AlexId] = alabama.TeamId,
                [DanielId] = alabama.TeamId,
            });

        // Georgia vs Kentucky: Michael/Alyson pick Georgia, Alex/Daniel pick Kentucky, Dance has
        // no pick - the "no pick, show both sides" example.
        Game game4 = new(
            GameSetGameId: Guid.Parse("00000000-0000-0000-0003-000000000004"),
            Home: georgia,
            Away: kentucky,
            HomeRank: 2,
            AwayRank: null,
            KickoffUtc: kickoff4,
            PointValue: 10,
            Status: final ? GameStatus.Final : GameStatus.Scheduled,
            HomeScore: final ? 24 : null,
            AwayScore: final ? 21 : null,
            Period: null,
            Clock: null,
            WinnerTeamId: final ? georgia.TeamId : null,
            Picks: new Dictionary<Guid, Guid>
            {
                [MichaelId] = georgia.TeamId,
                [AlysonId] = georgia.TeamId,
                [AlexId] = kentucky.TeamId,
                [DanielId] = kentucky.TeamId,
                // Dance: no entry.
            });

        MemberRef[] activeAtLock = [Michael, Alyson, Dance, Alex, Daniel];

        List<DashboardGameDto> games = [];
        List<DashboardGameDto> everyoneAgrees = [];
        int pointsSoFar = 0;
        int maxRemaining = 0;

        foreach (Game game in new[] { game1, game2, game3, game4 })
        {
            DashboardGameDto dto = game.ToDto(_viewerId, activeAtLock);
            if (dto.MyOutcome == InfluenceOutcome.Won)
            {
                pointsSoFar += dto.Game.PointValue;
            }
            else if (dto.MyOutcome != InfluenceOutcome.NoPick && dto.Game.Status != GameStatus.Final && dto.Game.WinnerTeamId is null)
            {
                maxRemaining += dto.Game.PointValue;
            }

            if (dto.OppositeCount == 0 && dto.MyOutcome != InfluenceOutcome.NoPick)
            {
                everyoneAgrees.Add(dto);
            }
            else
            {
                games.Add(dto);
            }
        }

        // Ordering: OppositeCount desc, PointValue desc, KickoffUtc asc, GameSetGameId asc
        // (04-Domain-Algorithms.md section 6 / D-099).
        DashboardGameDto[] ordered = [.. games
            .OrderByDescending(g => g.OppositeCount)
            .ThenByDescending(g => g.Game.PointValue)
            .ThenBy(g => g.Game.KickoffUtc)
            .ThenBy(g => g.Game.GameSetGameId)];

        DashboardGameDto[] orderedAgree = [.. everyoneAgrees
            .OrderByDescending(g => g.Game.PointValue)
            .ThenBy(g => g.Game.KickoffUtc)
            .ThenBy(g => g.Game.GameSetGameId)];

        return new DashboardResponse(
            IsAvailable: true,
            LockAtUtc: kickoff1,
            LockAtEasternDisplay: "Sat 12:00 PM ET",
            PointsSoFar: pointsSoFar,
            MaxRemaining: maxRemaining,
            ScoresMayBeStale: stale,
            Games: ordered,
            EveryoneAgrees: orderedAgree);
    }

    private static TeamDto Team(string school, string abbreviation) =>
        new(Guid.NewGuid(), school, abbreviation, ConferenceId: null, LogoUrl: null);

    /// <summary>Mutable in-memory shape of one seeded game, converted to a <see cref="DashboardGameDto"/> per viewer.</summary>
    private sealed record Game(
        Guid GameSetGameId,
        TeamDto Home,
        TeamDto Away,
        int? HomeRank,
        int? AwayRank,
        DateTimeOffset KickoffUtc,
        int PointValue,
        GameStatus Status,
        int? HomeScore,
        int? AwayScore,
        byte? Period,
        string? Clock,
        Guid? WinnerTeamId,
        Dictionary<Guid, Guid> Picks)
    {
        public DashboardGameDto ToDto(Guid viewerId, MemberRef[] activeAtLock)
        {
            GameSetGameDto gameDto = new(
                GameSetGameId: GameSetGameId,
                GameId: GameSetGameId,
                HomeTeam: Home,
                AwayTeam: Away,
                HomeRank: HomeRank,
                AwayRank: AwayRank,
                KickoffUtc: KickoffUtc,
                PointValue: PointValue,
                IsPointValueElevated: false,
                Source: GameSetGameSource.Rule,
                Status: Status,
                HomeScore: HomeScore,
                AwayScore: AwayScore,
                Period: Period,
                Clock: Clock,
                IsVoided: false,
                WinnerTeamId: WinnerTeamId);

            Guid? myTeamId = Picks.TryGetValue(viewerId, out Guid picked) ? picked : null;

            List<MemberRef> opposite = [];
            List<MemberRef> noPick = [];
            List<MemberRef> homePickers = [];
            List<MemberRef> awayPickers = [];

            foreach (MemberRef member in activeAtLock)
            {
                if (member.MembershipId == viewerId)
                {
                    continue;
                }

                if (!Picks.TryGetValue(member.MembershipId, out Guid theirPick))
                {
                    noPick.Add(member);
                    continue;
                }

                if (theirPick == Home.TeamId)
                {
                    homePickers.Add(member);
                }
                else
                {
                    awayPickers.Add(member);
                }

                if (myTeamId is { } mine && theirPick != mine)
                {
                    opposite.Add(member);
                }
            }

            InfluenceOutcome outcome;
            if (myTeamId is null)
            {
                outcome = InfluenceOutcome.NoPick;
            }
            else if (WinnerTeamId is { } winner)
            {
                outcome = winner == myTeamId ? InfluenceOutcome.Won : InfluenceOutcome.Lost;
            }
            else
            {
                outcome = InfluenceOutcome.Pending;
            }

            return new DashboardGameDto(
                Game: gameDto,
                MyTeamId: myTeamId,
                MyOutcome: outcome,
                OppositeCount: opposite.Count,
                OppositePicks: [.. opposite],
                NoPick: [.. noPick],
                HomePickers: [.. homePickers],
                AwayPickers: [.. awayPickers],
                SwingPoints: PointValue * opposite.Count);
        }
    }
}
