using NcaafPickEm.Domain.Scoring;
using NcaafPickEm.Domain.Tests.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.Scoring;

/// <summary>
/// Builds scoring inputs from names, so a test reads like the story - "Alyson picked Michigan and
/// Michigan won" - instead of carrying Guid variables around. Every id is derived from its name,
/// so a failing assertion is reproducible between runs.
/// </summary>
internal static class ScoringBuilder
{
    public static Guid MemberId(string name) => TestIds.Of($"member:{name}");

    public static Guid TeamId(string name) => TestIds.Of($"team:{name}");

    public static Guid GameSetGameId(string label) => TestIds.Of($"gameSetGame:{label}");

    /// <summary>A membership the lock job settled. Pass a status to model one it did not.</summary>
    public static ScoringMember Member(string name, SubmissionStatus status = SubmissionStatus.Locked) =>
        new(MemberId(name), status);

    /// <summary>A game nobody has played yet.</summary>
    public static ScoringGame Scheduled(string label, string home, string away, int points = 10) =>
        Game(label, home, away, points, GameStatus.Scheduled, null, null);

    /// <summary>A game that finished with the given score.</summary>
    public static ScoringGame Final(
        string label,
        string home,
        string away,
        int homeScore,
        int awayScore,
        int points = 10) =>
        Game(label, home, away, points, GameStatus.Final, homeScore, awayScore);

    /// <summary>One row of the week's set.</summary>
    public static ScoringGame Game(
        string label,
        string home,
        string away,
        int points = 10,
        GameStatus status = GameStatus.Scheduled,
        int? homeScore = null,
        int? awayScore = null,
        string? overrideWinner = null,
        bool isVoided = false,
        bool isRemoved = false) =>
        new(
            GameSetGameId: GameSetGameId(label),
            HomeTeamId: TeamId(home),
            AwayTeamId: TeamId(away),
            PointValue: points,
            Status: status,
            HomeScore: homeScore,
            AwayScore: awayScore,
            ResultOverrideWinnerTeamId: overrideWinner is null ? null : TeamId(overrideWinner),
            IsVoided: isVoided,
            IsRemoved: isRemoved);

    /// <summary>One member's pick of one team in one game.</summary>
    public static ScoringPick Pick(string memberName, string gameLabel, string teamName) =>
        new(MemberId(memberName), GameSetGameId(gameLabel), TeamId(teamName));

    /// <summary>A request over the given games, members and picks.</summary>
    public static WeekScoringRequest Request(
        IEnumerable<ScoringGame> games,
        IEnumerable<ScoringMember> members,
        IEnumerable<ScoringPick>? picks = null) =>
        new()
        {
            Games = [.. games],
            Members = [.. members],
            Picks = [.. picks ?? []],
        };

    /// <summary>The score the result carries for one member, by name.</summary>
    public static MemberWeekScore ScoreFor(this WeekScoreResult result, string memberName) =>
        result.Members.Single(score => score.MembershipId == MemberId(memberName));
}
