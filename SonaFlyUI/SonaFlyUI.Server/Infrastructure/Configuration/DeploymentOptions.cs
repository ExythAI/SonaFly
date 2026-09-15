using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
// Both namespaces define IPNetwork; ForwardedHeadersOptions.KnownNetworks holds this one.
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace SonaFlyUI.Server.Infrastructure.Configuration;

/// <summary>
/// How this instance is exposed to the network. Bound from the <c>SonaFly:Deployment</c>
/// configuration section.
/// </summary>
public sealed class DeploymentOptions
{
    public const string SectionName = "SonaFly:Deployment";

    /// <summary>
    /// True when the application is reached over HTTPS — directly, or through a TLS
    /// reverse proxy that sets X-Forwarded-Proto. Turns on redirection and HSTS.
    ///
    /// Leave false for a trusted local-only HTTP deployment; turning it on without
    /// working TLS in front makes the site unreachable, since every request redirects.
    /// </summary>
    public bool UseHttps { get; set; }

    /// <summary>Send Strict-Transport-Security. Only meaningful with <see cref="UseHttps"/>.</summary>
    public bool EnableHsts { get; set; } = true;

    /// <summary>
    /// HSTS lifetime. Kept modest by default: a browser honours it for this long even if
    /// the deployment later loses TLS, so a long value is hard to walk back.
    /// </summary>
    public int HstsMaxAgeDays { get; set; } = 30;

    /// <summary>
    /// Addresses of the reverse proxies whose forwarded headers are trusted, e.g.
    /// "172.18.0.2". Anything not listed here (or in <see cref="KnownNetworks"/>) has its
    /// X-Forwarded-* headers ignored, so a client cannot spoof its own address or scheme.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// Networks of trusted proxies in CIDR form, e.g. "172.18.0.0/16" for a Docker
    /// bridge network whose proxy address is not fixed.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// Whether the operator configured a proxy allowlist at all. Any entry counts, blank
    /// ones included: a blank-only list is a misconfiguration that
    /// <see cref="ForwardedHeadersConfiguration.Build"/> rejects at startup, rather than
    /// something to silently treat as "no proxy configured".
    /// </summary>
    public bool HasTrustedProxy => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

public static class ForwardedHeadersConfiguration
{
    /// <summary>
    /// Builds forwarded-header options from configuration.
    ///
    /// The framework default trusts only loopback, which is wrong in a container: the
    /// proxy is a peer on a bridge network, not localhost. The defaults are therefore
    /// cleared and replaced with exactly what the operator declared, so that an
    /// untrusted client cannot forge X-Forwarded-For or X-Forwarded-Proto and make the
    /// application believe a plain HTTP request arrived over TLS.
    ///
    /// An empty effective allowlist would mean "trust every peer", so a list that is
    /// configured but contributes no usable entry fails startup instead. Blank entries are
    /// rejected individually, because silently dropping them is how an allowlist ends up
    /// empty without anyone noticing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A configured address or network is blank or unparseable, or the resulting allowlist is empty.
    /// </exception>
    public static ForwardedHeadersOptions Build(DeploymentOptions deployment)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost
        };

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxy in deployment.KnownProxies)
        {
            if (string.IsNullOrWhiteSpace(proxy))
            {
                throw new InvalidOperationException(
                    $"{DeploymentOptions.SectionName}:KnownProxies contains a blank entry. Remove it, or " +
                    "remove the setting entirely to run without a trusted proxy.");
            }
            if (!IPAddress.TryParse(proxy.Trim(), out var address))
            {
                throw new InvalidOperationException(
                    $"{DeploymentOptions.SectionName}:KnownProxies contains '{proxy}', which is not an IP address.");
            }
            options.KnownProxies.Add(address);
        }

        foreach (var network in deployment.KnownNetworks)
        {
            if (string.IsNullOrWhiteSpace(network))
            {
                throw new InvalidOperationException(
                    $"{DeploymentOptions.SectionName}:KnownNetworks contains a blank entry. Remove it, or " +
                    "remove the setting entirely to run without a trusted proxy.");
            }
            options.KnownNetworks.Add(ParseNetwork(network.Trim()));
        }

        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            // UseForwardedHeaders with both lists empty trusts every peer, which is the
            // opposite of what an operator asking for a proxy allowlist wants.
            throw new InvalidOperationException(
                $"{DeploymentSectionProxySettings} are configured but produced an empty trust list. " +
                "Forwarded headers would then be accepted from any client, so startup is refused.");
        }

        return options;
    }

    private const string DeploymentSectionProxySettings =
        DeploymentOptions.SectionName + ":KnownProxies / " + DeploymentOptions.SectionName + ":KnownNetworks";

    private static IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var prefix) ||
            !int.TryParse(parts[1], out var length))
        {
            throw new InvalidOperationException(
                $"{DeploymentOptions.SectionName}:KnownNetworks contains '{cidr}', which is not CIDR notation (for example 172.18.0.0/16).");
        }

        var maxLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (length < 0 || length > maxLength)
        {
            throw new InvalidOperationException(
                $"{DeploymentOptions.SectionName}:KnownNetworks contains '{cidr}', whose prefix length must be between 0 and {maxLength}.");
        }

        return new IPNetwork(prefix, length);
    }
}
