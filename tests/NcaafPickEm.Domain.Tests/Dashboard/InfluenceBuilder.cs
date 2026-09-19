using NcaafPickEm.Domain.Dashboard;
using NcaafPickEm.Domain.Tests.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Dashboard;

/// <summary>
/// Builds influence inputs from names, so a test reads like the story - "Dance picked Texas" -
/// instead of carrying Guid variables around. Every id is derived from its name, so a failing
/// assertion is reproducible between runs.
/// </summary>
internal static class InfluenceBuilder
{
    /// <summary>The default kickoff, for tests where the time does not matter.</summary>
    public static readonly DateTime Noon = new(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);

    public static Guid MemberId(string name) => TestIds.Of($"member:{name}");

    public static Guid TeamId(string name) => TestIds.Of($"team:{name}");

    public static Guid GameSetGameId(string label) => TestIds.Of($"gameSetGame:{label}");

    /// <summary>A member of the league at lock. <paramref name="isFormer"/> marks one who has since left.</summary>
    public static InfluenceMember Member(string name, bool isFormer = false) =>
        new(MemberId(name), name, isFormer);

    /// <summary>
    /// One game in the set. Scores are only read when <paramref name="status"/> is Final; an
    /// <paramref name="overrideWinner"/> beats them either way.
    /// </summary>
    public static InfluenceGame Game(
        string label,
        string home,
        string away,
        int points = 10,
        DateTime? kickoff = null,
        GameStatus status = GameStatus.Scheduled,
        int? homeScore = null,
        int? awayScore = null,
        string? overrideWinner = null,
        bool isVoided = false) =>
        new(
            GameSetGameId: GameSetGameId(label),
            GameId: TestIds.Of($"game:{label}"),
            HomeTeamId: TeamId(home),
            AwayTeamId: TeamId(away),
            KickoffUtc: kickoff ?? Noon,
            PointValue: points,
            Status: status,
            HomeScore: homeScore,
            AwayScore: awayScore,
            ResultOverrideWinnerTeamId: overrideWinner is null ? null : TeamId(overrideWinner),
            IsVoided: isVoided);

    /// <summary>One member's pick of one team in one game.</summary>
    public static InfluencePick Pick(string memberName, string gameLabel, string teamName) =>
        new(MemberId(memberName), GameSetGameId(gameLabel), TeamId(teamName));

    /// <summary>
    /// A request over the given games and picks, with <paramref name="viewer"/> plus
    /// <paramref name="members"/> as the memberships active at lock, in that order.
    /// </summary>
    public static InfluenceRequest Request(
        string viewer,
        IEnumerable<string> members,
        IEnumerable<InfluenceGame> games,
        IEnumerable<InfluencePick> picks) =>
        new(
            ViewerMembershipId: MemberId(viewer),
            Games: [.. games],
            MembersActiveAtLock: [.. members.Select(name => Member(name))],
            Picks: [.. picks]);

    /// <summary>The display names a result listed, for comparing against the story's own lists.</summary>
    public static IReadOnlyList<string> Names(IReadOnlyList<InfluenceMember> members) =>
        [.. members.Select(member => member.DisplayName)];
}
