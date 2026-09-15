using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Runs requests through the real Program.cs pipeline rather than calling controllers
/// directly, because the things that go wrong in a deployment are not in the controllers:
/// middleware ordering, JWT wiring, cookie attributes, which endpoints authentication
/// actually covers, and whether the health probe still means what it claims once HTTPS is
/// switched on.
/// </summary>
public sealed class HostPipelineTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"sonafly-host-{Guid.NewGuid():N}.db");

    private WebApplicationFactory<Program> _plainHttp = null!;
    private const string AdminPassword = "Correct-Horse-9";

    /// <summary>
    /// A stand-in for the built single-page app.
    ///
    /// Without it the SPA fallback has no file to serve and answers 404 on its own, which
    /// would make the "an API path is not answered with the page" tests pass whether the
    /// behaviour is right or not. The whole point is that a real deployment *does* have an
    /// index.html to fall into.
    /// </summary>
    private readonly string _webRoot =
        Path.Combine(Path.GetTempPath(), $"sonafly-webroot-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_webRoot);
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "index.html"),
            "<!DOCTYPE html><html><body>SonaFly web app</body></html>");

        _plainHttp = Host();
    }

    /// <summary>
    /// A host configured the way an operator would, with any extra settings layered on top.
    /// Staging rather than Development, so the pipeline under test is the deployed one
    /// (HSTS, the production warnings, no CORS hole) without triggering Production's refusal
    /// to start on a chosen admin password.
    /// </summary>
    private WebApplicationFactory<Program> Host(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Staging);
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_databasePath}");
            builder.UseSetting("Jwt:Secret", "SonaFly-Host-Test-Secret-Key-At-Least-32-Chars!");
            builder.UseSetting("Jwt:Issuer", "SonaFly");
            builder.UseSetting("Jwt:Audience", "SonaFlyClients");
            builder.UseSetting("SonaFly:AdminDefaultPassword", AdminPassword);
            // TestServer has no listener, so UseHttpsRedirection cannot infer a port and
            // would quietly pass every request through. Naming one makes the redirect
            // behave as it does behind a real proxy.
            builder.UseSetting("https_port", "443");
            builder.UseWebRoot(_webRoot);

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static HttpClient NoRedirects(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string ClientHeaderName => "X-SonaFly-Client";

    /// <summary>Signs in as the seeded administrator and returns the whole response.</summary>
    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, bool useCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "admin", password = AdminPassword, useCookie })
        };
        request.Headers.Add(ClientHeaderName, "web");
        return await client.SendAsync(request);
    }

    private static async Task<LoginResponse> LoginBodyAsync(HttpClient client, bool useCookie)
    {
        using var response = await LoginAsync(client, useCookie);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    // ── The health probe ──

    [Fact]
    public async Task Health_IsAnonymousAndReportsTheDatabaseCheck()
    {
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"database\":\"Healthy\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_StillChecksTheDatabaseWhenHttpsIsEnabled()
    {
        // The container probe reaches the app over plain HTTP on the internal port. If the
        // HTTPS redirect stood in front of it, the probe would get a 307 — which curl
        // treats as success — and "healthy" would stop meaning the database is reachable.
        using var https = Host(("SonaFly:Deployment:UseHttps", "true"));
        using var client = NoRedirects(https);

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"database\":\"Healthy\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EverythingElse_IsRedirectedToHttpsWhenHttpsIsEnabled()
    {
        using var https = Host(("SonaFly:Deployment:UseHttps", "true"));
        using var client = NoRedirects(https);

        using var response = await client.GetAsync("/api/albums");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith("https://", response.Headers.Location!.ToString());
    }

    // ── API routes never return the web app's HTML ──

    [Fact]
    public async Task AGetToAPostOnlyEndpoint_IsNotAnsweredWithTheSinglePageApp()
    {
        // Exactly what a native client does after following a 301 from a plain-HTTP URL:
        // the redirect turns its POST into a GET. Answering that with index.html and a 200
        // gave the client HTML to parse as JSON, and an error message about '<' that named
        // nothing relevant.
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/api/auth/login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<!DOCTYPE", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownApiPath_Is404RatherThanThePage()
    {
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/api/no-such-thing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ANonApiPath_DoesFallBackToThePage()
    {
        // The control for the two tests above: the fallback is wired up and serving, so
        // their 404s are the API exclusion working rather than a missing file.
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/albums/some-client-side-route");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SonaFly web app", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnknownHubPath_Is404RatherThanThePage()
    {
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/hubs/no-such-hub");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Authentication ──

    [Fact]
    public async Task AnApiRoute_RefusesAnUnauthenticatedRequest()
    {
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/api/albums");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ABearerTokenFromLogin_AuthenticatesTheNextRequest()
    {
        using var client = NoRedirects(_plainHttp);
        var login = await LoginBodyAsync(client, useCookie: false);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AForgedToken_IsRejected()
    {
        using var client = NoRedirects(_plainHttp);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── The refresh cookie ──

    [Fact]
    public async Task ACookieLogin_PutsTheRefreshTokenOnlyInAnHttpOnlyCookie()
    {
        using var client = NoRedirects(_plainHttp);

        using var response = await LoginAsync(client, useCookie: true);
        var body = await response.Content.ReadAsStringAsync();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        // Nothing script-readable: not in the body, and not readable from document.cookie.
        Assert.DoesNotContain("\"refreshToken\":\"", body.Replace("\"refreshToken\":null", ""));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRefreshCookie_IsMarkedSecureWhenTheDeploymentUsesHttps()
    {
        using var https = Host(("SonaFly:Deployment:UseHttps", "true"));
        using var client = https.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await LoginAsync(client, useCookie: true);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARefreshFromTheCookie_RotatesTheSession()
    {
        using var client = NoRedirects(_plainHttp);
        using (var login = await LoginAsync(client, useCookie: true))
        {
            login.EnsureSuccessStatusCode();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add(ClientHeaderName, "web");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A rotation, so the browser is handed the replacement cookie.
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ARefreshFromTheCookieWithoutTheClientHeader_IsRefused()
    {
        // The cookie alone must not be enough: the header is the second lock behind
        // SameSite, so a cross-origin form post cannot rotate somebody's session.
        using var client = NoRedirects(_plainHttp);
        using (var login = await LoginAsync(client, useCookie: true))
        {
            login.EnsureSuccessStatusCode();
        }

        using var response = await client.PostAsJsonAsync("/api/auth/refresh", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ALogout_ClearsTheCookieAndEndsTheSession()
    {
        using var client = NoRedirects(_plainHttp);
        var login = await LoginBodyAsync(client, useCookie: false);

        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
               {
                   Content = JsonContent.Create(new { refreshToken = login.RefreshToken })
               })
        {
            (await client.SendAsync(logout)).EnsureSuccessStatusCode();
        }

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken = login.RefreshToken })
        };
        using var response = await client.SendAsync(refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Forwarded headers ──

    [Fact]
    public async Task AnUntrustedClient_CannotClaimItsRequestArrivedOverHttps()
    {
        // With no proxy allowlist the middleware is not registered at all, so this header
        // is inert. If it were honoured, a plain HTTP request could earn a Secure cookie
        // and skip the redirect.
        using var https = Host(("SonaFly:Deployment:UseHttps", "true"));
        using var client = NoRedirects(https);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/albums");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    [Fact]
    public void ABlankProxyAllowlist_RefusesToStart()
    {
        // Fail closed: an empty effective allowlist means "trust every peer".
        using var misconfigured = Host(("SonaFly:Deployment:KnownProxies:0", ""));

        var failure = Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());

        Assert.Contains("blank entry", Flatten(failure));
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : exception.Message + " " + Flatten(exception.InnerException);

    // ── Security headers ──

    [Fact]
    public async Task EveryResponse_CarriesTheHardeningHeaders()
    {
        using var client = NoRedirects(_plainHttp);

        using var response = await client.GetAsync("/api/health");

        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    // ── The password-change gate ──

    [Fact]
    public async Task AnAccountOwingANewPassword_CanChangeItAndNothingElse()
    {
        using var client = NoRedirects(_plainHttp);

        // The seeded administrator starts out holding a password somebody else chose.
        using (var scope = _plainHttp.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();
            var admin = db.Users.Single(u => u.UserName == "admin");
            admin.MustChangePassword = true;
            await db.SaveChangesAsync();
        }

        var login = await LoginBodyAsync(client, useCookie: false);
        Assert.True(login.User.MustChangePassword);

        using (var browse = new HttpRequestMessage(HttpMethod.Get, "/api/albums"))
        {
            browse.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            using var refused = await client.SendAsync(browse);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = AdminPassword, newPassword = "Another-Horse-9" })
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var changed = await client.SendAsync(change);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        // The change hands back replacement credentials, and they work immediately.
        var replacement = (await changed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();
        using var retry = new HttpRequestMessage(HttpMethod.Get, "/api/albums");
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacement);
        using var allowed = await client.SendAsync(retry);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    public Task DisposeAsync()
    {
        _plainHttp.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_databasePath); } catch (IOException) { /* temp file */ }
        try { Directory.Delete(_webRoot, recursive: true); } catch (IOException) { /* temp dir */ }
        return Task.CompletedTask;
    }
}
