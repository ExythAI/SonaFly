using SonaFlyUI.Server.Domain.Entities;

namespace SonaFlyUI.Server.Application.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}

public interface ITokenService
{
    string CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
}
