using System.Text.Json.Serialization;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// JS-interop-only shape returned by <c>wwwroot/js/visibility.js</c>'s <c>subscribe(...)</c>
/// (P6-03). Carries an explicit <see cref="JsonPropertyNameAttribute"/> rather than relying on
/// Blazor's default interop JSON naming convention, the same discipline <c>PushInterop.cs</c>
/// uses for the JS shapes it wraps.
/// </summary>
public sealed record VisibilityState(
    [property: JsonPropertyName("isVisible")] bool IsVisible);
