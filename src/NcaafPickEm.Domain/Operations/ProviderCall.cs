namespace NcaafPickEm.Domain.Operations;

/// <summary>
/// One outbound call to an external provider (Feature 12). The monthly CFBD free-tier count is
/// <c>COUNT(*) WHERE Provider = 'Cfbd'</c> over the month.
/// </summary>
public sealed class ProviderCall
{
    /// <summary>Maximum length of <see cref="Provider"/>, in characters.</summary>
    public const int ProviderMaxLength = 40;

    /// <summary>Maximum length of <see cref="Operation"/>, in characters.</summary>
    public const int OperationMaxLength = 100;

    /// <summary>Maximum length of <see cref="Error"/>, in characters.</summary>
    public const int ErrorMaxLength = 500;

    public Guid Id { get; set; }

    /// <summary>"Cfbd", "Espn", or "Fixture".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider method called, e.g. "GetGames".</summary>
    public string Operation { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }

    public int DurationMs { get; set; }

    public bool Success { get; set; }

    public int? StatusCode { get; set; }

    public string? Error { get; set; }
}
