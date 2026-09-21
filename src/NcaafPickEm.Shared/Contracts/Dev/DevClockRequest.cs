namespace NcaafPickEm.Shared.Contracts.Dev;

/// <summary>
/// Body of <c>PUT /api/admin/fixture/clock</c> (P10-01). Every field is optional. A true
/// <see cref="Frozen"/> applies first, then <see cref="NowUtc"/>, then <see cref="Advance"/>, then
/// a false <see cref="Frozen"/> - so "this instant, frozen" lands exactly there, and "this
/// instant, running" runs on from there. A body with nothing set is a 400.
/// </summary>
/// <param name="NowUtc">The instant the app should believe it is right now.</param>
/// <param name="Advance">How far to move the clock; negative moves it backwards.</param>
/// <param name="Frozen">True stops the clock; false lets it run again from its reading.</param>
public sealed record DevClockRequest(
    DateTimeOffset? NowUtc = null,
    TimeSpan? Advance = null,
    bool? Frozen = null);
