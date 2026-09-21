using Microsoft.AspNetCore.HttpOverrides;

namespace NcaafPickEm.Api.Hosting;

/// <summary>
/// Reverse-proxy support for the Docker deployment (P8-05, D-160).
/// </summary>
/// <remarks>
/// On the home server the container listens on plain HTTP on <c>127.0.0.1:5000</c> and
/// <c>tailscale serve</c> terminates TLS on the host and forwards to it. Without this the app
/// sees every request as <c>http://127.0.0.1:5000</c> from the Docker gateway address, which
/// breaks three things at once: absolute links the app builds from the request (invite URLs,
/// when <c>App:PublicOrigin</c> is not set) name the loopback port, <c>Secure</c> cookies look
/// like they are being set over plain HTTP, and the per-IP rate limiter (D-153) partitions the
/// whole family into one bucket, so one window trips for everybody at once.
/// <para>
/// Off unless <c>App__BehindProxy</c> is <c>true</c>: trusting <c>X-Forwarded-*</c> from an
/// untrusted caller lets it spoof its own client IP, so a run that binds HTTPS directly - a
/// Visual Studio debug session - must not switch it on.
/// </para>
/// </remarks>
public static class ForwardedHeadersSetup
{
    /// <summary>Configuration key that turns proxy-header handling on. Default false.</summary>
    public const string BehindProxyKey = "App:BehindProxy";

    /// <summary>Binds <see cref="ForwardedHeadersOptions"/> when <see cref="BehindProxyKey"/> is set.</summary>
    public static IServiceCollection AddAppForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!IsBehindProxy(configuration))
        {
            return services;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // The proxy is `tailscale serve` on the container host, so it connects from the
            // Docker bridge gateway - an address that is assigned at network-create time and
            // changes whenever the compose project is recreated, which makes a KnownProxies
            // entry unmaintainable. Clearing both lists is safe *here* because the container
            // port is published on 127.0.0.1 only (see the compose file's UFW/iptables comment):
            // nothing off the box can open a socket to it, so there is no untrusted caller whose
            // X-Forwarded-For we could be believing. (KnownIPNetworks, not the KnownNetworks
            // property every older sample uses: that one is ASPDEPR005-obsolete in .NET 10 and
            // this repo builds warnings-as-errors.)
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            // One hop (Tailscale Serve). The default is already 1; stated so a second proxy is a
            // deliberate change rather than something that quietly starts being trusted.
            options.ForwardLimit = 1;
        });

        return services;
    }

    /// <summary>
    /// Inserts the forwarded-headers middleware when <see cref="BehindProxyKey"/> is set.
    /// Must run before request logging, authentication, and the rate limiter, all of which read
    /// the values it rewrites.
    /// </summary>
    public static WebApplication UseAppForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (IsBehindProxy(app.Configuration))
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    private static bool IsBehindProxy(IConfiguration configuration) =>
        configuration.GetValue<bool?>(BehindProxyKey) ?? false;
}
