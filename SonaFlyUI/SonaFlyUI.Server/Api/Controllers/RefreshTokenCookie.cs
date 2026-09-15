namespace SonaFlyUI.Server.Api.Controllers;

/// <summary>
/// The browser-side home for the refresh token.
///
/// Native clients keep receiving the refresh token in the response body and store it in
/// the platform keychain. A browser cannot hold a seven-day credential safely in any
/// script-readable store, so for web callers the token only ever exists as an HttpOnly
/// cookie that JavaScript — including injected script — cannot read.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "sonafly_rt";

    /// <summary>
    /// Limits the cookie to the endpoints that consume it, so it is not attached to
    /// media, artwork or hub requests.
    /// </summary>
    public const string Path = "/api/auth";

    /// <summary>
    /// Required on cookie-authenticated refresh and logout calls. A custom header cannot
    /// be set by a cross-site form or image, and forces a CORS preflight on fetch, so it
    /// backs up SameSite as CSRF protection for the one cookie-authenticated mutation.
    /// </summary>
    public const string ClientHeader = "X-SonaFly-Client";

    public static void Append(HttpResponse response, string refreshToken, DateTime expiresUtc) =>
        response.Cookies.Append(Name, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            // Only over TLS where TLS is in use. Production deployments are expected to
            // terminate HTTPS; see the deployment section of README.md.
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
            IsEssential = true
        });

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = Path
        });

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>
    /// Whether a cookie-bearing request carries the custom header that a cross-site
    /// caller cannot forge.
    /// </summary>
    public static bool HasClientHeader(HttpRequest request) =>
        request.Headers.ContainsKey(ClientHeader);
}
