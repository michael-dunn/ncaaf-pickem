namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// A betting line for a game at a point in time. History is kept; the "current" line for a game
/// is the row with the newest <see cref="FetchedUtc"/>.
/// </summary>
public sealed class GameLine
{
    /// <summary>Maximum length of <see cref="Provider"/>, in characters.</summary>
    public const int ProviderMaxLength = 40;

    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    /// <summary>Sportsbook name as CFBD reports it, e.g. "consensus".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Home minus away. Negative means the home team is favored.</summary>
    public decimal Spread { get; set; }

    public DateTime FetchedUtc { get; set; }
}
