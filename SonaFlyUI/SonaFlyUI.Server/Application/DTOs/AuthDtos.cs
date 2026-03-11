namespace SonaFlyUI.Server.Application.DTOs;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresUtc, UserInfoDto User);
public record RefreshRequest(string RefreshToken);
public record RefreshResponse(string AccessToken, string RefreshToken, DateTime ExpiresUtc);
public record LogoutRequest(string RefreshToken);
