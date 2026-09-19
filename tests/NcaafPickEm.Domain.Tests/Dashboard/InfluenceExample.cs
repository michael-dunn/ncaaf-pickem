using NcaafPickEm.Domain.Dashboard;
using NcaafPickEm.Fixtures;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Dashboard;

/// <summary>
/// The Overview worked example as it sits in
/// <c>tests/NcaafPickEm.Fixtures/Data/influence-example.json</c>, turned into an
/// <see cref="InfluenceRequest"/> so the test asserts the documented dashboards themselves rather
/// than a hand-retyped copy of them.
/// </summary>
internal static class InfluenceExample
{
    /// <summary>The first game's kickoff. The second is an hour later.</summary>
    private static readonly DateTime FirstKickoff = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);

    /// <summary>The fixture, deserialized.</summary>
    public static InfluenceExampleFile File { get; } =
        FixtureLoader.Read<InfluenceExampleFile>("influence-example.json");

    /// <summary>
    /// Builds the request for one viewer. Both games are worth 10 points and neither has kicked
    /// off, exactly as the fixture's own note says. The games keep fixture order with kickoffs an
    /// hour apart, which is what settles the tie in Alyson's dashboard, where both games have one
    /// opposite pick and the same point value.
    /// </summary>
    public static InfluenceRequest RequestFor(string viewerName) => new(
        ViewerMembershipId: InfluenceBuilder.MemberId(viewerName),
        Games:
        [
            .. File.Games.Select((game, index) => InfluenceBuilder.Game(
                game.Label,
                home: game.HomeTeam,
                away: game.AwayTeam,
                points: game.PointValue,
                kickoff: FirstKickoff.AddHours(index),
                status: GameStatus.Scheduled)),
        ],
        MembersActiveAtLock: [.. File.Members.Select(name => InfluenceBuilder.Member(name))],
        Picks:
        [
            .. File.Games.SelectMany(game => game.Picks.SelectMany(byTeam =>
                byTeam.Value.Select(memberName =>
                    InfluenceBuilder.Pick(memberName, game.Label, byTeam.Key)))),
        ]);

    /// <summary>The dashboard the fixture documents for one viewer.</summary>
    public static IReadOnlyList<InfluenceExampleDashboardGame> ExpectedFor(string viewerName) =>
        File.ExpectedDashboards[viewerName];
}

/// <summary>The shape of <c>influence-example.json</c>.</summary>
internal sealed record InfluenceExampleFile(
    IReadOnlyList<string> Members,
    IReadOnlyList<InfluenceExampleGame> Games,
    Dictionary<string, IReadOnlyList<InfluenceExampleDashboardGame>> ExpectedDashboards);

/// <summary>One matchup in the fixture, with its picks keyed by team name.</summary>
internal sealed record InfluenceExampleGame(
    string Label,
    string HomeTeam,
    string AwayTeam,
    int PointValue,
    Dictionary<string, IReadOnlyList<string>> Picks);

/// <summary>One documented dashboard row.</summary>
internal sealed record InfluenceExampleDashboardGame(
    string Label,
    string MyTeam,
    IReadOnlyList<string> OppositePicks);
