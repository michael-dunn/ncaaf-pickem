namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// Configuration for <see cref="CfbdReferenceDataProvider"/>, bound from the <c>Cfbd</c>
/// configuration section (<c>Cfbd__ApiKey</c> as an environment variable).
/// </summary>
public sealed class CfbdOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Cfbd";

    /// <summary>
    /// The CollegeFootballData API key, sent as a bearer token. Never logged or committed; the
    /// operator's key lives outside the repo (see <c>AGENT-NOTES.md</c>).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
