using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Operations;

/// <summary>
/// Last-known freshness of one slice of provider data, shown on the data status page (Feature 09).
/// One row per <see cref="RefreshDataType"/>; the enum is the primary key.
/// </summary>
public sealed class DataRefreshStatus
{
    /// <summary>Maximum length of <see cref="LastError"/>, in characters.</summary>
    public const int LastErrorMaxLength = 1000;

    public RefreshDataType DataType { get; set; }

    public DateTime? LastSuccessUtc { get; set; }

    public DateTime? LastAttemptUtc { get; set; }

    public string? LastError { get; set; }
}
