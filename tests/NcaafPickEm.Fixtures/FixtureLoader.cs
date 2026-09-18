using System.Reflection;
using System.Text.Json;

namespace NcaafPickEm.Fixtures;

/// <summary>
/// Reads JSON fixtures embedded from <c>tests/NcaafPickEm.Fixtures/Data</c>.
/// Fixture names are the path under <c>Data</c> with forward slashes, e.g.
/// <c>"Cfbd/games-2026-week7.json"</c>.
/// </summary>
/// <remarks>P2-05 populates <c>Data/</c> with the Week 7, 2026 sample week.</remarks>
public static class FixtureLoader
{
    private const string ResourcePrefix = "NcaafPickEm.Fixtures.Data.";

    private const string RealResourcePrefix = "NcaafPickEm.Fixtures.Real.";

    private static readonly Assembly Assembly = typeof(FixtureLoader).Assembly;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Every fixture name currently embedded, in sorted order.</summary>
    public static IReadOnlyList<string> Names { get; } = Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(ToFixtureName)
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Reads a fixture as raw text.</summary>
    /// <exception cref="FileNotFoundException">No fixture with that name is embedded.</exception>
    public static string ReadText(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string resourceName = ToResourceName(name);
        using Stream? stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new FileNotFoundException(
                $"Fixture '{name}' is not embedded. Known fixtures: {string.Join(", ", Names)}.",
                name);
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Every captured real provider response currently embedded, in sorted order. These are the
    /// trimmed P2-01 captures under <c>Real/</c>, kept apart from the synthetic sample week.
    /// </summary>
    public static IReadOnlyList<string> RealNames { get; } = Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(RealResourcePrefix, StringComparison.Ordinal))
        .Select(name => name[RealResourcePrefix.Length..])
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Reads a captured real provider response as raw text, e.g.
    /// <c>"espn-scoreboard-20260912.json"</c>.
    /// </summary>
    /// <param name="name">File name under <c>Real/</c>.</param>
    /// <exception cref="FileNotFoundException">No such capture is embedded.</exception>
    public static string ReadRealText(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using Stream? stream = Assembly.GetManifestResourceStream(RealResourcePrefix + name);
        if (stream is null)
        {
            throw new FileNotFoundException(
                $"Capture '{name}' is not embedded. Known captures: {string.Join(", ", RealNames)}.",
                name);
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads a captured real provider response with web (camelCase) JSON options.</summary>
    /// <param name="name">File name under <c>Real/</c>.</param>
    /// <typeparam name="T">The shape to deserialize into.</typeparam>
    /// <exception cref="FileNotFoundException">No such capture is embedded.</exception>
    /// <exception cref="InvalidOperationException">The capture deserialized to null.</exception>
    public static T ReadReal<T>(string name)
    {
        string json = ReadRealText(name);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Capture '{name}' deserialized to null.");
    }

    /// <summary>Reads a fixture and deserializes it with web (camelCase) JSON options.</summary>
    /// <exception cref="FileNotFoundException">No fixture with that name is embedded.</exception>
    /// <exception cref="InvalidOperationException">The fixture deserialized to null.</exception>
    public static T Read<T>(string name)
    {
        string json = ReadText(name);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Fixture '{name}' deserialized to null.");
    }

    private static string ToResourceName(string fixtureName)
    {
        string trimmed = fixtureName.Replace('\\', '/').TrimStart('/');
        int lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return ResourcePrefix + trimmed;
        }

        // Embedded resource names replace directory separators with dots but keep the file extension.
        string directory = trimmed[..lastSlash].Replace('/', '.');
        string file = trimmed[(lastSlash + 1)..];
        return $"{ResourcePrefix}{directory}.{file}";
    }

    private static string ToFixtureName(string resourceName)
    {
        string relative = resourceName[ResourcePrefix.Length..];
        int lastDot = relative.LastIndexOf('.');
        if (lastDot < 0)
        {
            return relative;
        }

        string withoutExtension = relative[..lastDot];
        string extension = relative[lastDot..];
        return withoutExtension.Replace('.', '/') + extension;
    }
}
