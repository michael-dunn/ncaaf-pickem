namespace NcaafPickEm.Domain.Seasons;

/// <summary>
/// One slot in one poll for one week. Only the AP Top 25 is ingested today, but the poll name is
/// part of the key so another poll can be added without a schema change.
/// </summary>
public sealed class Ranking
{
    /// <summary>Maximum length of <see cref="Poll"/>, in characters.</summary>
    public const int PollMaxLength = 10;

    /// <summary>The only poll ingested today.</summary>
    public const string ApPoll = "AP";

    public int SeasonYear { get; set; }

    public int Week { get; set; }

    public string Poll { get; set; } = ApPoll;

    /// <summary>1 through 25.</summary>
    public int Rank { get; set; }

    public Guid TeamId { get; set; }

    public Team? Team { get; set; }

    public DateTime FetchedUtc { get; set; }
}
