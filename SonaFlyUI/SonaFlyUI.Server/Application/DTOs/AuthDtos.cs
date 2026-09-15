namespace SonaFlyUI.Server.Application.DTOs;

/// <param name="UseCookie">
/// Set by browser clients. The refresh token is then returned as an HttpOnly cookie and
/// omitted from the response body, so no script-readable copy of it ever exists.
/// Native clients leave this false and store the body value in the platform keychain.
/// </param>
public record LoginRequest(string Username, string Password, bool UseCookie = false);

/// <param name="RefreshToken">
/// Null for cookie-based (browser) sessions, where the value is in the HttpOnly cookie.
/// </param>
public record LoginResponse(string AccessToken, string? RefreshToken, DateTime ExpiresUtc, UserInfoDto User);

/// <param name="RefreshToken">
/// Omitted by browser clients, which present the HttpOnly cookie instead.
/// </param>
/// <param name="UseCookie">
/// Set by a browser that is upgrading a legacy body-stored session: it presents the old
/// token once, and the rotated replacement is returned as a cookie instead of in the body.
/// </param>
public record RefreshRequest(string? RefreshToken = null, bool UseCookie = false);

/// <inheritdoc cref="LoginResponse"/>
public record RefreshResponse(string AccessToken, string? RefreshToken, DateTime ExpiresUtc);

/// <param name="RefreshToken">
/// Omitted by browser clients, which present the HttpOnly cookie instead.
/// </param>
public record LogoutRequest(string? RefreshToken = null);
