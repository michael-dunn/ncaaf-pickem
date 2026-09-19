using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>Result of one <c>POST /api/admin/refresh/{dataType}</c> call, run synchronously.</summary>
/// <param name="DataType">Which slice was refreshed.</param>
/// <param name="Success">Whether it succeeded.</param>
/// <param name="Error">The failure, or null on success.</param>
public sealed record ManualRefreshResponse(RefreshDataType DataType, bool Success, string? Error);
