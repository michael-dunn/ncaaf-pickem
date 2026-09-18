using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// One scheduled game. CollegeFootballData is the source of truth for the schedule; ESPN
/// supplies live score and status once <see cref="EspnEventId"/> is matched (Feature 12).
/// </summary>
public sealed class Game
{
    /// <summary>Maximum length of <see cref="Clock"/>, in characters.</summary>
    public const int ClockMaxLength = 8;

    /// <summary>Maximum length of <see cref="Venue"/>, in characters.</summary>
    public const int VenueMaxLength = 100;

    public Guid Id { get; set; }

    /// <summary>CollegeFootballData's game id. The natural key for schedule upserts.</summary>
    public long CfbdGameId { get; set; }

    /// <summary>ESPN's event id, filled by the matcher in P2-03. Null until matched.</summary>
    public long? EspnEventId { get; set; }

    public int SeasonYear { get; set; }

    /// <summary>The provider's week number, not the league's.</summary>
    public int Week { get; set; }

    public Guid HomeTeamId { get; set; }

    public Team? HomeTeam { get; set; }

    public Guid AwayTeamId { get; set; }

    public Team? AwayTeam { get; set; }

    public DateTime KickoffUtc { get; set; }

    /// <summary>Calendar date of <see cref="KickoffUtc"/> in America/New_York, computed at ingest.</summary>
    public DateOnly KickoffEasternDate { get; set; }

    /// <summary>True when <see cref="KickoffEasternDate"/> is a Saturday. Computed at ingest.</summary>
    public bool IsSaturdayEastern { get; set; }

    public bool IsConferenceGame { get; set; }

    public GameStatus Status { get; set; }

    public int? HomeScore { get; set; }

    public int? AwayScore { get; set; }

    /// <summary>Quarter (or overtime period) while in progress.</summary>
    public byte? Period { get; set; }

    /// <summary>Game clock as the provider reports it, e.g. "12:04".</summary>
    public string? Clock { get; set; }

    public string? Venue { get; set; }

    public DateTime? LastScoreUpdateUtc { get; set; }
}
