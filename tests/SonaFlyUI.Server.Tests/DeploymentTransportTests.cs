using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using SonaFlyUI.Server.Api.Middleware;
using SonaFlyUI.Server.Infrastructure.Configuration;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Covers what the application must get right to sit safely behind a TLS reverse proxy:
/// trusting forwarded headers from the proxy and only the proxy, and not leaking
/// ticketed URLs to other origins.
/// </summary>
public class DeploymentTransportTests
{
    // ── Forwarded headers ──

    [Fact]
    public void NoConfiguredProxy_MeansNoneIsTrusted()
    {
        var deployment = new DeploymentOptions();

        Assert.False(deployment.HasTrustedProxy);
    }

    [Fact]
    public void ConfiguredProxies_ReplaceTheLoopbackOnlyDefault()
    {
        // The framework default trusts 127.0.0.1/::1, which is never the proxy's address
        // in a container. Leaving it in place would silently ignore the real proxy.
        var options = ForwardedHeadersConfiguration.Build(new DeploymentOptions
        {
            KnownProxies = ["172.28.0.2"],
            KnownNetworks = ["172.28.0.0/16"],
        });

        Assert.Equal(IPAddress.Parse("172.28.0.2"), Assert.Single(options.KnownProxies));
        var network = Assert.Single(options.KnownNetworks);
        Assert.Equal(IPAddress.Parse("172.28.0.0"), network.Prefix);
        Assert.Equal(16, network.PrefixLength);
    }

    [Fact]
    public void ForwardedHeaders_CoverSchemeHostAndClientAddress()
    {
        var options = ForwardedHeadersConfiguration.Build(
            new DeploymentOptions { KnownProxies = ["172.28.0.2"] });

        // Proto is the one that decides IsHttps, which drives redirection, HSTS and the
        // Secure flag on the refresh cookie.
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("172.28.0.999")]
    public void AMalformedProxyAddress_FailsStartup(string proxy)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersConfiguration.Build(new DeploymentOptions { KnownProxies = [proxy] }));

        Assert.Contains("KnownProxies", ex.Message);
    }

    [Theory]
    [InlineData("172.28.0.0")]      // no prefix length
    [InlineData("172.28.0.0/abc")]
    [InlineData("172.28.0.0/33")]   // out of range for IPv4
    public void AMalformedNetwork_FailsStartup(string network)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersConfiguration.Build(new DeploymentOptions { KnownNetworks = [network] }));

        Assert.Contains("KnownNetworks", ex.Message);
    }

    [Fact]
    public void BlankEntries_FailStartupRatherThanEmptyingTheAllowlist()
    {
        // Environment-variable configuration commonly yields empty strings. Dropping them
        // silently used to leave both lists empty, which UseForwardedHeaders reads as
        // "trust every peer" — the opposite of the fail-closed behaviour documented.
        var ex = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersConfiguration.Build(new DeploymentOptions
            {
                KnownProxies = ["", "  "],
                KnownNetworks = [""],
            }));

        Assert.Contains("blank entry", ex.Message);
    }

    [Fact]
    public void ABlankOnlyProxyList_IsStillTreatedAsConfigured()
    {
        // HasTrustedProxy gates the Build call, so it must not quietly answer "no proxy"
        // for a list that exists but is unusable; that would skip validation entirely.
        var deployment = new DeploymentOptions { KnownProxies = [""] };

        Assert.True(deployment.HasTrustedProxy);
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersConfiguration.Build(deployment));
    }

    [Fact]
    public void AMixOfBlankAndValidEntries_FailsRatherThanSilentlyNarrowing()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersConfiguration.Build(new DeploymentOptions
            {
                KnownProxies = ["172.28.0.2", ""],
            }));

        Assert.Contains("KnownProxies", ex.Message);
    }

    [Fact]
    public void NullOrDefaultConfiguration_LeavesForwardingOffEntirely()
    {
        // No allowlist at all is a valid deployment (direct exposure), and must not throw:
        // Program.cs simply never calls Build.
        Assert.False(new DeploymentOptions().HasTrustedProxy);
    }

    // ── Defaults ──

    [Fact]
    public void HttpsIsOffByDefault_SoAnHttpDeploymentDoesNotRedirectIntoALoop()
    {
        var deployment = new DeploymentOptions();

        Assert.False(deployment.UseHttps);
        // ... but HSTS is on once HTTPS is declared, so operators do not have to set two flags.
        Assert.True(deployment.EnableHsts);
        Assert.Equal(30, deployment.HstsMaxAgeDays);
    }

    // ── Security headers ──

    private static async Task<IHeaderDictionary> HeadersAfterMiddlewareAsync()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);
        return context.Response.Headers;
    }

    [Fact]
    public async Task ReferrerPolicy_KeepsTicketedUrlsFromLeavingTheOrigin()
    {
        var headers = await HeadersAfterMiddlewareAsync();

        // Stream URLs carry ?ticket=... because a media element cannot send a bearer
        // header. Without this they would be sent to any third-party origin.
        Assert.Equal("same-origin", headers["Referrer-Policy"]);
    }

    [Fact]
    public async Task TheUsualHardeningHeadersArePresent()
    {
        var headers = await HeadersAfterMiddlewareAsync();

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("same-origin", headers["Cross-Origin-Opener-Policy"]);
    }

    [Fact]
    public async Task HeadersAreSetBeforeTheRestOfThePipelineRuns()
    {
        // They must be on the response before anything can start writing a body,
        // otherwise a streamed response would go out without them.
        var context = new DefaultHttpContext();
        string? referrerPolicyDuringNext = null;
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            referrerPolicyDuringNext = ctx.Response.Headers["Referrer-Policy"];
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("same-origin", referrerPolicyDuringNext);
    }
}
