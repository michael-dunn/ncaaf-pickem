using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>
/// Freshness of one slice of provider data (<c>DataRefreshStatus</c>).
/// </summary>
/// <param name="DataType">Which slice.</param>
/// <param name="LastSuccessUtc">When it last refreshed successfully; null until it ever has.</param>
/// <param name="LastAttemptUtc">When it was last attempted, successfully or not.</param>
/// <param name="LastError">The last failure's message, or null when the last attempt succeeded.</param>
public sealed record RefreshStatusDto(
    RefreshDataType DataType,
    DateTimeOffset? LastSuccessUtc,
    DateTimeOffset? LastAttemptUtc,
    string? LastError);
