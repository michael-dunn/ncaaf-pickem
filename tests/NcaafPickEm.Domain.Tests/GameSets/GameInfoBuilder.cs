using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Tests.GameSets;

/// <summary>
/// Builds a <see cref="GameInfo"/> that is eligible by default - Saturday Eastern, both teams FBS,
/// scheduled - so each test only states the one thing it is about.
/// </summary>
internal sealed class GameInfoBuilder
{
    private readonly string _awayTeam;
    private readonly string _homeTeam;
    private string? _awayConference;
    private string? _homeConference;
    private TeamClassification _awayClassification = TeamClassification.Fbs;
    private TeamClassification _homeClassification = TeamClassification.Fbs;
    private DateTime _kickoffUtc = GameSetTimes.SaturdayAfternoon;
    private bool _isSaturdayEastern = true;
    private bool _isConferenceGame;
    private GameStatus _status = GameStatus.Scheduled;

    private GameInfoBuilder(string awayTeam, string homeTeam)
    {
        _awayTeam = awayTeam;
        _homeTeam = homeTeam;
    }

    /// <summary>"Georgia at Alabama", the way the schedule reads.</summary>
    public static GameInfoBuilder Game(string awayTeam, string homeTeam) => new(awayTeam, homeTeam);

    public GameInfoBuilder InConference(string conference)
    {
        _awayConference = conference;
        _homeConference = conference;
        _isConferenceGame = true;
        return this;
    }

    public GameInfoBuilder HomeInConference(string conference)
    {
        _homeConference = conference;
        return this;
    }

    public GameInfoBuilder AwayInConference(string conference)
    {
        _awayConference = conference;
        return this;
    }

    public GameInfoBuilder AwayIsFcs()
    {
        _awayClassification = TeamClassification.Fcs;
        return this;
    }

    public GameInfoBuilder HomeIsFcs()
    {
        _homeClassification = TeamClassification.Fcs;
        return this;
    }

    public GameInfoBuilder KickingOffAt(DateTime kickoffUtc)
    {
        _kickoffUtc = kickoffUtc;
        return this;
    }

    /// <summary>A Friday night Eastern kickoff: the ingest flag says it is not a Saturday game.</summary>
    public GameInfoBuilder OnFridayEastern()
    {
        _kickoffUtc = GameSetTimes.FridayNightEastern;
        _isSaturdayEastern = false;
        return this;
    }

    /// <summary>A Friday night Pacific kickoff, which is Saturday 12:30 AM Eastern.</summary>
    public GameInfoBuilder OnFridayPacific()
    {
        _kickoffUtc = GameSetTimes.FridayNightPacific;
        _isSaturdayEastern = true;
        return this;
    }

    public GameInfoBuilder WithStatus(GameStatus status)
    {
        _status = status;
        return this;
    }

    public GameInfo Build() => new(
        GameId: TestIds.Of($"{_awayTeam} at {_homeTeam}"),
        HomeTeamId: TestIds.Of(_homeTeam),
        AwayTeamId: TestIds.Of(_awayTeam),
        HomeConferenceId: _homeConference is null ? null : TestIds.Of(_homeConference),
        AwayConferenceId: _awayConference is null ? null : TestIds.Of(_awayConference),
        HomeClassification: _homeClassification,
        AwayClassification: _awayClassification,
        KickoffUtc: _kickoffUtc,
        IsSaturdayEastern: _isSaturdayEastern,
        IsConferenceGame: _isConferenceGame,
        Status: _status);
}
