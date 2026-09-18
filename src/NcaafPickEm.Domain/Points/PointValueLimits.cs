using NcaafPickEm.Domain.Leagues;

namespace NcaafPickEm.Domain.Points;

/// <summary>
/// The legal range for every point value in the app: the league default, a point rule's value, and
/// a commissioner's per-game override (Feature 03). Aliased from
/// <see cref="League.MinPointValue"/> / <see cref="League.MaxPointValue"/> so the two can never
/// drift apart.
/// </summary>
public static class PointValueLimits
{
    /// <summary>Lowest legal point value.</summary>
    public const int Min = League.MinPointValue;

    /// <summary>Highest legal point value.</summary>
    public const int Max = League.MaxPointValue;

    /// <summary>True when <paramref name="pointValue"/> lies within <see cref="Min"/>..<see cref="Max"/>.</summary>
    /// <param name="pointValue">The value to check.</param>
    /// <returns>Whether the value is legal.</returns>
    public static bool IsValid(int pointValue) => pointValue is >= Min and <= Max;
}
