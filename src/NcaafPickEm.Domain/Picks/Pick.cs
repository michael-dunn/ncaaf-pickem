using NcaafPickEm.Domain.GameSets;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// One member's choice of winner for one game in one week (Feature 04). Picks survive the game
/// being removed or the member being removed, so history stays intact.
/// </summary>
public sealed class Pick
{
    public Guid Id { get; set; }

    public Guid MembershipId { get; set; }

    public Membership? Membership { get; set; }

    public Guid WeekGameSetGameId { get; set; }

    public WeekGameSetGame? WeekGameSetGame { get; set; }

    /// <summary>Must be the home or away team of the underlying game.</summary>
    public Guid PickedTeamId { get; set; }

    public Team? PickedTeam { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
