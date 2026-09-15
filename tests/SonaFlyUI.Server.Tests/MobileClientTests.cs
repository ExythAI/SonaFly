using System.Net;
using System.Net.Http.Json;
using SonaFly.Models;
using SonaFly.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests
{
    public class MobileClientTests
    {
        private static ServerStorageService Storage()
        {
            Preferences.Clear();
            SecureStorage.Clear();
            var storage = new ServerStorageService();
            storage.Add(new ServerConfig { Id = "test", BaseUrl = "https://example.test",
                IsActive = true, AccessToken = "old", RefreshToken = "refresh-old" });
            return storage;
        }

        private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
        }

        [Fact]
        public async Task ConcurrentStreamingRequestsRefreshOnlyOnce()
        {
            var storage = Storage();
            var refreshes = 0;
            using var http = new HttpClient(new Handler(async request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
                {
                    Interlocked.Increment(ref refreshes);
                    await Task.Delay(30);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                        { accessToken = "new", refreshToken = "refresh-new", expiresUtc = DateTime.UtcNow.AddMinutes(30) }) };
                }
                if (request.Headers.Authorization?.Parameter != "new")
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                    { url = "/api/stream/tracks/track?ticket=scoped" }) };
            }));
            var api = new SonaFlyApiClient(http, storage);
            var urls = await Task.WhenAll(api.GetStreamUrlAsync(Guid.NewGuid()), api.GetStreamUrlAsync(Guid.NewGuid()));
            Assert.Equal(1, refreshes);
            Assert.All(urls, url => Assert.Equal("https://example.test/api/stream/tracks/track?ticket=scoped", url));
        }

        [Fact]
        public async Task RefreshCannotRestoreLoggedOutMobileSession()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(async _ =>
            {
                // Logout lands while the refresh is in flight.
                await storage.ClearTokensAsync("test");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                    { accessToken = "new", refreshToken = "refresh-new", expiresUtc = DateTime.UtcNow.AddMinutes(30) }) };
            }));
            Assert.False(await new SonaFlyApiClient(http, storage).TryRefreshTokenAsync());
            Assert.Null(storage.GetActive()!.AccessToken);
        }

        // ── Credential storage ──

        [Fact]
        public async Task TokensAreKeptInTheKeychainAndNeverInPreferences()
        {
            var storage = Storage();

            await storage.UpdateTokensAsync("test", "access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(30));

            var preferences = string.Join(' ', Preferences.Snapshot().Values);
            Assert.DoesNotContain("access-secret", preferences);
            Assert.DoesNotContain("refresh-secret", preferences);
            // ... but the server metadata is still there.
            Assert.Contains("https://example.test", preferences);

            var keychain = string.Join(' ', SecureStorage.Snapshot().Values);
            Assert.Contains("refresh-secret", keychain);
        }

        [Fact]
        public async Task TokensSurviveAnAppRestart()
        {
            var storage = Storage();
            await storage.UpdateTokensAsync("test", "access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(30));

            // A fresh process: Preferences and the keychain persist, the in-memory cache does not.
            var restarted = new ServerStorageService();
            await restarted.LoadAsync();

            var server = restarted.GetActive();
            Assert.NotNull(server);
            Assert.Equal("access-secret", server.AccessToken);
            Assert.Equal("refresh-secret", server.RefreshToken);
        }

        [Fact]
        public async Task TokensStoredByAnEarlierVersionAreMovedOutOfPreferences()
        {
            Preferences.Clear();
            SecureStorage.Clear();

            // Exactly what the previous version wrote: tokens inside the Preferences blob.
            Preferences.Set("sonafly_servers",
                """
                [{"Id":"legacy","Name":"Home","BaseUrl":"https://example.test","IsActive":true,
                  "AccessToken":"old-access","RefreshToken":"old-refresh"}]
                """);

            var storage = new ServerStorageService();
            await storage.LoadAsync();

            // The session still works ...
            var server = storage.GetActive();
            Assert.NotNull(server);
            Assert.Equal("old-refresh", server.RefreshToken);

            // ... and the plaintext copies are gone from Preferences.
            var preferences = string.Join(' ', Preferences.Snapshot().Values);
            Assert.DoesNotContain("old-refresh", preferences);
            Assert.DoesNotContain("old-access", preferences);
            Assert.Contains("old-refresh", string.Join(' ', SecureStorage.Snapshot().Values));
        }

        [Fact]
        public async Task ClearingTokensErasesThemFromTheKeychain()
        {
            var storage = Storage();
            await storage.UpdateTokensAsync("test", "access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(30));

            await storage.ClearTokensAsync("test");

            Assert.Empty(SecureStorage.Snapshot());
            Assert.DoesNotContain("refresh-secret", string.Join(' ', Preferences.Snapshot().Values));

            var restarted = new ServerStorageService();
            await restarted.LoadAsync();
            Assert.Null(restarted.GetActive()!.RefreshToken);
        }

        // ── Cross-server isolation ──

        /// <summary>Two configured servers; "a" starts active.</summary>
        private static ServerStorageService TwoServers()
        {
            Preferences.Clear();
            SecureStorage.Clear();
            var storage = new ServerStorageService();
            storage.Add(new ServerConfig { Id = "a", BaseUrl = "https://server-a.test",
                IsActive = true, AccessToken = "token-a", RefreshToken = "refresh-a" });
            storage.Add(new ServerConfig { Id = "b", BaseUrl = "https://server-b.test",
                IsActive = false, AccessToken = "token-b", RefreshToken = "refresh-b" });
            return storage;
        }

        [Fact]
        public async Task ASecondServerNeverReceivesTheFirstServersToken()
        {
            var storage = TwoServers();
            var delivered = new List<(string Host, string? Token)>();
            var gate = new TaskCompletionSource();

            using var http = new HttpClient(new Handler(async request =>
            {
                lock (delivered)
                {
                    delivered.Add((request.RequestUri!.Host, request.Headers.Authorization?.Parameter));
                }
                // Hold server A's request open so the switch to B happens mid-flight.
                if (request.RequestUri.Host == "server-a.test") await gate.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { url = "/api/stream/tracks/t?ticket=scoped" })
                };
            }));

            var api = new SonaFlyApiClient(http, storage);

            var slowRequestToA = api.GetStreamUrlAsync(Guid.NewGuid());
            // The user switches servers while that request is still out.
            storage.SetActive("b");
            var requestToB = api.GetStreamUrlAsync(Guid.NewGuid());

            gate.SetResult();
            var urls = await Task.WhenAll(slowRequestToA, requestToB);

            // Each host only ever saw its own token.
            Assert.All(delivered, d => Assert.Equal(
                d.Host == "server-a.test" ? "token-a" : "token-b", d.Token));

            // And the ticket is resolved against the server that issued it, not whichever
            // server happened to be active when the response came back.
            Assert.Equal("https://server-a.test/api/stream/tracks/t?ticket=scoped", urls[0]);
            Assert.Equal("https://server-b.test/api/stream/tracks/t?ticket=scoped", urls[1]);
        }

        [Fact]
        public async Task CredentialsTravelOnTheRequest_NotOnSharedClientDefaults()
        {
            // The concrete defect: ApplyAuth() used to write the active server's token to
            // HttpClient.DefaultRequestHeaders, which every in-flight request shares. Two
            // overlapping requests against different servers then raced over one header.
            // Pinning this is deterministic, unlike trying to lose the race on purpose.
            var storage = TwoServers();
            using var http = new HttpClient(new Handler(request =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { url = "/api/stream/tracks/t?ticket=scoped" })
                })));

            var api = new SonaFlyApiClient(http, storage);
            await api.GetStreamUrlAsync(Guid.NewGuid());
            await api.ChangePasswordAsync("Correct-Horse-9", "Different-Horse-8");

            Assert.Null(http.DefaultRequestHeaders.Authorization);
        }

        [Fact]
        public async Task SwitchingServersMidRequestAbandonsTheRefreshInsteadOfRetargetingIt()
        {
            var storage = TwoServers();
            var refreshHosts = new List<string>();
            var switched = new TaskCompletionSource();

            using var http = new HttpClient(new Handler(async request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
                {
                    lock (refreshHosts) { refreshHosts.Add(request.RequestUri.Host); }
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                        { accessToken = "rotated", refreshToken = "refresh-rotated", expiresUtc = DateTime.UtcNow.AddMinutes(30) }) };
                }

                // Server A rejects the token, then the user switches away before the
                // client gets a chance to refresh.
                storage.SetActive("b");
                switched.TrySetResult();
                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }));

            var api = new SonaFlyApiClient(http, storage);

            await Assert.ThrowsAnyAsync<HttpRequestException>(() => api.GetStreamUrlAsync(Guid.NewGuid()));
            await switched.Task;

            // No refresh was attempted at all: the server the 401 came from is no longer
            // active, and rotating against server B would have spent A's credentials there.
            Assert.Empty(refreshHosts);
            // Server B's stored session is untouched.
            Assert.Equal("refresh-b", storage.GetAll().Single(s => s.Id == "b").RefreshToken);
        }

        [Fact]
        public async Task ARefreshIsStoredAgainstTheServerItWasIssuedBy()
        {
            var storage = TwoServers();
            using var http = new HttpClient(new Handler(request =>
            {
                Assert.Equal("server-a.test", request.RequestUri!.Host);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                    { accessToken = "rotated-a", refreshToken = "refresh-rotated-a", expiresUtc = DateTime.UtcNow.AddMinutes(30) }) });
            }));

            Assert.True(await new SonaFlyApiClient(http, storage).TryRefreshTokenAsync());

            var a = storage.GetAll().Single(s => s.Id == "a");
            var b = storage.GetAll().Single(s => s.Id == "b");
            Assert.Equal("rotated-a", a.AccessToken);
            // The inactive server keeps its own credentials.
            Assert.Equal("token-b", b.AccessToken);
            Assert.Equal("refresh-b", b.RefreshToken);
        }

        [Fact]
        public async Task ConcurrentRequestsRotateOncePerServer()
        {
            var storage = TwoServers();
            var refreshesByHost = new Dictionary<string, int>();

            using var http = new HttpClient(new Handler(async request =>
            {
                var host = request.RequestUri!.Host;
                if (request.RequestUri.AbsolutePath == "/api/auth/refresh")
                {
                    lock (refreshesByHost)
                    {
                        refreshesByHost[host] = refreshesByHost.GetValueOrDefault(host) + 1;
                    }
                    await Task.Delay(30);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                        { accessToken = $"new-{host}", refreshToken = $"refresh-new-{host}", expiresUtc = DateTime.UtcNow.AddMinutes(30) }) };
                }

                return request.Headers.Authorization?.Parameter == $"new-{host}"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { url = "/api/stream/tracks/t?ticket=scoped" }) }
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }));

            var api = new SonaFlyApiClient(http, storage);
            await Task.WhenAll(api.GetStreamUrlAsync(Guid.NewGuid()), api.GetStreamUrlAsync(Guid.NewGuid()));

            Assert.Equal(1, refreshesByHost["server-a.test"]);
            Assert.False(refreshesByHost.ContainsKey("server-b.test"));
        }

        [Fact]
        public async Task RemovingAServerErasesItsKeychainEntry()
        {
            var storage = Storage();
            await storage.UpdateTokensAsync("test", "access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(30));

            await storage.RemoveAsync("test");

            Assert.Empty(SecureStorage.Snapshot());
        }

        // ── Signing out ──

        [Fact]
        public async Task ASlowKeychainWriteCannotResurrectASignedOutSession()
        {
            var storage = Storage();

            // Hold the write open, sign out underneath it, then let it go.
            var released = new TaskCompletionSource();
            SecureStorage.BeforeWrite = () => released.Task;
            var pendingWrite = storage.UpdateTokensAsync(
                "test", "access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(30));

            SecureStorage.BeforeWrite = null;
            var signOut = storage.ClearTokensAsync("test");

            released.SetResult();
            await pendingWrite;
            await signOut;

            // The write belonged to a session that has since ended; putting it back on disk
            // would hand the next start a credential the user believes they gave up.
            Assert.Empty(SecureStorage.Snapshot());

            var restarted = new ServerStorageService();
            await restarted.LoadAsync();
            Assert.Null(restarted.GetActive()!.RefreshToken);
            Assert.Null(restarted.GetActive()!.AccessToken);
        }

        [Fact]
        public async Task AFailedKeychainRemovalIsReportedRatherThanSwallowed()
        {
            var storage = Storage();
            await storage.UpdateTokensAsync("test", "access-secret", "refresh-secret", DateTime.UtcNow.AddMinutes(30));

            SecureStorage.RemovalFails = true;
            var cleared = await storage.ClearTokensAsync("test");
            SecureStorage.RemovalFails = false;

            Assert.False(cleared);
            // In-memory state is gone either way: the app stops using the session at once.
            Assert.Null(storage.GetActive()!.RefreshToken);
        }

        [Fact]
        public async Task LogoutTellsTheOriginalServerAndSendsItsOwnRefreshToken()
        {
            var storage = Storage();
            string? sentTo = null;
            string? sentBody = null;
            using var http = new HttpClient(new Handler(async request =>
            {
                sentTo = request.RequestUri!.ToString();
                sentBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));

            Assert.True(await new SonaFlyApiClient(http, storage).LogoutAsync());

            // Clearing tokens locally leaves the refresh family usable for a week; the
            // server has to be told.
            Assert.Equal("https://example.test/api/auth/logout", sentTo);
            Assert.Contains("refresh-old", sentBody);
        }

        [Fact]
        public async Task LogoutStillReportsFailureWhenTheServerIsUnreachable()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));

            // Signing out has to work on a plane; the caller clears local state regardless.
            Assert.False(await new SonaFlyApiClient(http, storage).LogoutAsync());
        }

        // ── Changing the password ──

        private static HttpResponseMessage ChangedWith(string access, string refresh) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    accessToken = access, refreshToken = refresh, expiresUtc = DateTime.UtcNow.AddMinutes(30)
                })
            };

        [Fact]
        public async Task ChangingThePasswordKeepsTheReplacementCredentials()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(request =>
                Task.FromResult(request.RequestUri!.AbsolutePath == "/api/auth/change-password"
                    ? ChangedWith("access-after", "refresh-after")
                    : new HttpResponseMessage(HttpStatusCode.OK))));

            var result = await new SonaFlyApiClient(http, storage).ChangePasswordAsync("old", "Correct-Horse-9");

            // The server revoked every token issued under the old password, including the
            // one this request used. Ignoring the body leaves the app holding dead tokens.
            Assert.Equal(ChangePasswordResult.Changed, result);
            Assert.Equal("access-after", storage.GetActive()!.AccessToken);
            Assert.Equal("refresh-after", storage.GetActive()!.RefreshToken);
        }

        [Fact]
        public async Task TheReplacementCredentialsSurviveARestart()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(_ =>
                Task.FromResult(ChangedWith("access-after", "refresh-after"))));

            await new SonaFlyApiClient(http, storage).ChangePasswordAsync("old", "Correct-Horse-9");

            var restarted = new ServerStorageService();
            await restarted.LoadAsync();
            Assert.Equal("refresh-after", restarted.GetActive()!.RefreshToken);
        }

        [Fact]
        public async Task ChangingThePasswordClearsTheTemporaryPasswordFlag()
        {
            var storage = Storage();
            storage.SetMustChangePassword("test", true);
            using var http = new HttpClient(new Handler(_ =>
                Task.FromResult(ChangedWith("access-after", "refresh-after"))));

            await new SonaFlyApiClient(http, storage).ChangePasswordAsync("old", "Correct-Horse-9");

            Assert.False(storage.GetActive()!.MustChangePassword);
        }

        [Fact]
        public async Task AWrongCurrentPasswordIsReportedAsRejected()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))));

            var result = await new SonaFlyApiClient(http, storage).ChangePasswordAsync("wrong", "Correct-Horse-9");

            Assert.Equal(ChangePasswordResult.Rejected, result);
            // Nothing was replaced, so the existing session must be untouched.
            Assert.Equal("old", storage.GetActive()!.AccessToken);
        }

        [Fact]
        public async Task SigningOutWhileTheChangeIsInFlightDoesNotReviveTheSession()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(async _ =>
            {
                await storage.ClearTokensAsync("test");
                return ChangedWith("access-after", "refresh-after");
            }));

            var result = await new SonaFlyApiClient(http, storage).ChangePasswordAsync("old", "Correct-Horse-9");

            Assert.Equal(ChangePasswordResult.ChangedButSignedOut, result);
            Assert.Null(storage.GetActive()!.RefreshToken);
        }

        [Fact]
        public async Task SwitchingServerWhileTheChangeIsInFlightDoesNotCrossCredentials()
        {
            var storage = Storage();
            storage.Add(new ServerConfig { Id = "other", BaseUrl = "https://other.test" });

            using var http = new HttpClient(new Handler(_ =>
            {
                storage.SetActive("other");
                return Task.FromResult(ChangedWith("access-after", "refresh-after"));
            }));

            var result = await new SonaFlyApiClient(http, storage).ChangePasswordAsync("old", "Correct-Horse-9");

            Assert.Equal(ChangePasswordResult.ChangedButSignedOut, result);
            // One server's credentials must never end up stored against another.
            Assert.Null(storage.GetAll().Single(s => s.Id == "other").RefreshToken);
        }

        [Fact]
        public async Task BrowsingWorksImmediatelyAfterAPasswordChange()
        {
            var storage = Storage();
            var authorizations = new List<string?>();
            using var http = new HttpClient(new Handler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/auth/change-password")
                    return Task.FromResult(ChangedWith("access-after", "refresh-after"));

                authorizations.Add(request.Headers.Authorization?.Parameter);
                // The server accepts only the credentials it just issued.
                return Task.FromResult(request.Headers.Authorization?.Parameter == "access-after"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                        { Content = JsonContent.Create(new { url = "/api/stream/tracks/track?ticket=scoped" }) }
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }));
            var api = new SonaFlyApiClient(http, storage);

            Assert.Equal(ChangePasswordResult.Changed, await api.ChangePasswordAsync("old", "Correct-Horse-9"));
            var url = await api.GetStreamUrlAsync(Guid.NewGuid());

            Assert.Equal("https://example.test/api/stream/tracks/track?ticket=scoped", url);
            // No sign-in round trip in between: the first attempt already carried the new token.
            Assert.Equal(["access-after"], authorizations);
        }

        // ── A server address without a scheme ──

        [Fact]
        public async Task ARedirectOnSignIn_IsExplainedRatherThanParsedAsJson()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(_ =>
            {
                // What a TLS-only server behind a proxy answers to a plain-HTTP POST.
                var moved = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                moved.Headers.Location = new Uri("https://music.example.test/api/auth/login");
                return Task.FromResult(moved);
            }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new SonaFlyApiClient(http, storage).LoginAsync("http://music.example.test", "admin", "pw"));

            // The old path followed the redirect, which rewrote the POST as a GET, got the
            // web app's HTML back and reported "ExpectedStartOfValueNotFound, <".
            Assert.Contains("https://", ex.Message);
            Assert.DoesNotContain("ExpectedStartOfValue", ex.Message);
        }

        [Fact]
        public async Task ARedirectOnAnOrdinaryRequest_IsExplainedTheSameWay()
        {
            var storage = Storage();
            using var http = new HttpClient(new Handler(_ =>
            {
                var moved = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                moved.Headers.Location = new Uri("https://example.test/api/auditoriums");
                return Task.FromResult(moved);
            }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new SonaFlyApiClient(http, storage).GetAuditoriumsAsync());

            Assert.Contains("redirected", ex.Message);
        }

        [Fact]
        public void LoginCarriesTheTemporaryPasswordFlagFromTheServer()
        {
            // The mobile UserInfo used to omit it, so a newly created or reset account went
            // straight into a shell where every request answers 403.
            var user = System.Text.Json.JsonSerializer.Deserialize<UserInfo>(
                """
                {"id":"11111111-1111-1111-1111-111111111111","userName":"new","email":"new@sonafly.local",
                 "displayName":"New","isEnabled":true,"roles":["User"],"createdUtc":"2026-01-01T00:00:00Z",
                 "mustChangePassword":true}
                """,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.True(user!.MustChangePassword);
        }
    }
}

// Test-only replacement for MAUI platform preferences; the real storage/client sources
// above are compiled unchanged so their HTTP behavior can be tested without Android.
namespace SonaFly.Services
{
    internal static class Preferences
    {
        private static readonly Dictionary<string, string> Values = new();
        public static string Get(string key, string fallback) => Values.GetValueOrDefault(key, fallback);
        public static void Set(string key, string value) => Values[key] = value;
        public static void Clear() => Values.Clear();

        /// <summary>Everything Preferences holds, for asserting what is NOT in it.</summary>
        public static IReadOnlyDictionary<string, string> Snapshot() => Values;
    }

    /// <summary>Test-only stand-in for the platform keychain.</summary>
    internal static class SecureStorage
    {
        private static readonly Dictionary<string, string> Values = new();

        /// <summary>
        /// Awaited before a write lands, so a test can hold one open and act while it is
        /// pending — which is exactly the window a sign-out has to survive.
        /// </summary>
        public static Func<Task>? BeforeWrite;

        /// <summary>Makes removal fail, standing in for a locked or unavailable keychain.</summary>
        public static bool RemovalFails;

        public static Task<string?> GetAsync(string key) =>
            Task.FromResult(Values.GetValueOrDefault(key));

        public static async Task SetAsync(string key, string value)
        {
            if (BeforeWrite is { } gate) await gate();
            Values[key] = value;
        }

        public static bool Remove(string key)
        {
            if (RemovalFails) throw new InvalidOperationException("The keychain is unavailable.");
            return Values.Remove(key);
        }

        public static void Clear()
        {
            Values.Clear();
            BeforeWrite = null;
            RemovalFails = false;
        }

        public static IReadOnlyDictionary<string, string> Snapshot() => Values;
    }
}
