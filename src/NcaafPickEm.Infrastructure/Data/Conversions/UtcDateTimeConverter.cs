using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NcaafPickEm.Infrastructure.Data.Conversions;

/// <summary>
/// Keeps every <see cref="DateTime"/> column in UTC in both directions (D-014).
/// </summary>
/// <remarks>
/// Writing: a Local value is converted, an Unspecified value is taken at face value as UTC (that
/// is what a value round-tripped through an older column or a JSON payload looks like), and a Utc
/// value passes through. Reading: SQL Server hands back Unspecified, so the Kind is stamped to Utc
/// and no caller can mistake a stored instant for local time.
/// </remarks>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    /// <summary>Creates the converter.</summary>
    public UtcDateTimeConverter()
        : base(
            value => ToUtc(value),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
