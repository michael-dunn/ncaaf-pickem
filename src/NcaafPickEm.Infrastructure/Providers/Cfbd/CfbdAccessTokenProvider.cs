using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;

namespace NcaafPickEm.Infrastructure.Providers.Cfbd;

/// <summary>
/// Hands the configured CFBD API key to <see cref="BaseBearerTokenAuthenticationProvider"/> as a
/// static bearer token. The Kiota-generated client needs an <see cref="IAccessTokenProvider"/>
/// but does not ship one itself (<c>Implementation/spikes/providers.md</c>) — this is that
/// ~15-line class.
/// </summary>
public sealed class CfbdAccessTokenProvider : IAccessTokenProvider
{
    private readonly IOptionsMonitor<CfbdOptions> _options;

    /// <summary>Creates the provider. Reads the key fresh from options on every call.</summary>
    public CfbdAccessTokenProvider(IOptionsMonitor<CfbdOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_options.CurrentValue.ApiKey);

    /// <summary>
    /// Every host is allowed: the request adapter is only ever pointed at the CFBD host
    /// (<see cref="CfbdReferenceDataProvider"/> never accepts a caller-supplied URL), so there is
    /// no second host this token could leak to.
    /// </summary>
    public AllowedHostsValidator AllowedHostsValidator { get; } = new();
}
