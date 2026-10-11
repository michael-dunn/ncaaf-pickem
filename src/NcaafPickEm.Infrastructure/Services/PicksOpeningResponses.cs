using NcaafPickEm.Domain.Picks;
using NcaafPickEm.Domain.Seasons;
using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Infrastructure.Services;

/// <summary>
/// Puts a <see cref="PicksOpening"/> on the week's game-set response (Feature 16, D-190), and
/// takes the provisional set off it for a member before picks open.
/// </summary>
public static class PicksOpeningResponses
{
    /// <summary>Copies the picks-open moment and state onto <paramref name="response"/>.</summary>
    /// <param name="response">The week's set.</param>
    /// <param name="opening">Where the week stands against its picks-open moment.</param>
    public static WeekGameSetResponse WithOpening(this WeekGameSetResponse response, PicksOpening opening)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(opening);

        return response with
        {
            PicksOpenAtUtc = opening.OpensAtUtc,
            PicksOpenAtEasternDisplay = opening.OpensAtUtc is DateTimeOffset opensAtUtc
                ? SeasonCalendar.EasternDisplay(opensAtUtc)
                : null,
            PicksOpen = opening.IsOpen,
        };
    }

    /// <summary>
    /// What a member (not a commissioner) sees: before picks open, no games and no lock time -
    /// the "Games will be set Tuesday" state - because the set is still the commissioner's preview.
    /// </summary>
    /// <param name="response">The week's set as the commissioner sees it.</param>
    public static WeekGameSetResponse ForMember(this WeekGameSetResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.PicksOpen || response.IsLocked
            ? response
            : response with { Games = [], LockAtUtc = null, LockAtEasternDisplay = null };
    }
}
