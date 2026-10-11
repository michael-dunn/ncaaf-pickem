using System.Globalization;

namespace NcaafPickEm.Web.Pages.Stats;

/// <summary>Text for the stats pages' values, shared by the league page and the member profile.</summary>
public static class StatFormat
{
    /// <summary>A percentage and its raw fraction, e.g. "68.4% (93/136)"; "– (0/0)" with no games.</summary>
    /// <param name="hits">The numerator.</param>
    /// <param name="total">The denominator.</param>
    public static string Percent(int hits, int total) =>
        total == 0
            ? "– (0/0)"
            : string.Create(CultureInfo.InvariantCulture, $"{100.0 * hits / total:0.0}% ({hits}/{total})");

    /// <summary>"+N more" for a highlight's tied weeks, or empty when there are none.</summary>
    /// <param name="otherTiedWeeks">The other tied weeks.</param>
    public static string MoreTies(int[] otherTiedWeeks) =>
        otherTiedWeeks.Length == 0 ? string.Empty : $"+{otherTiedWeeks.Length} more";
}
