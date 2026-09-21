namespace NcaafPickEm.Shared.Contracts.Dev;

/// <summary>The demo league's game set for the current week, as <see cref="DemoWeekResponse"/> reports it.</summary>
/// <param name="Week">Week number.</param>
/// <param name="GameCount">Games in the set that have not been removed.</param>
/// <param name="ActiveGameCount">Of those, games that are not voided.</param>
/// <param name="FinalCount">Active games whose status is Final.</param>
/// <param name="InProgressCount">Active games whose status is InProgress.</param>
/// <param name="LockAtUtc">Earliest kickoff among active games; null when the set is empty.</param>
/// <param name="LockAtEasternDisplay">Eastern rendering of <see cref="LockAtUtc"/>.</param>
/// <param name="LockedUtc">When the lock job ran; null until it has.</param>
/// <param name="IsFrozenNow">
/// True when picks are refused right now: the lock job has run, or <see cref="LockAtUtc"/> has
/// passed on the app clock.
/// </param>
/// <param name="IsComplete">True once every active game is Final and the week has been scored.</param>
public sealed record DemoWeekSetDto(
    int Week,
    int GameCount,
    int ActiveGameCount,
    int FinalCount,
    int InProgressCount,
    DateTimeOffset? LockAtUtc,
    string? LockAtEasternDisplay,
    DateTimeOffset? LockedUtc,
    bool IsFrozenNow,
    bool IsComplete);
