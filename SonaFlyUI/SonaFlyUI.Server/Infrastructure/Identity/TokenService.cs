using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;

namespace SonaFlyUI.Server.Infrastructure.Identity;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SonaFly";
    public string Audience { get; set; } = "SonaFlyClients";
    public int AccessTokenExpirationMinutes { get; set; } = 30;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

public class TokenService : ITokenService
{
    /// <summary>
    /// Present and "true" while the holder has not replaced a bootstrap or
    /// administrator-assigned password. See <see cref="SonaFlyUI.Server.Api.Middleware.PasswordChangeRequiredMiddleware"/>.
    /// </summary>
    public const string MustChangePasswordClaim = "must_change_password";

    /// <summary>
    /// The holder's Identity security stamp at the time the token was issued. Every
    /// request re-checks it, so rotating the stamp invalidates tokens already in the wild.
    /// </summary>
    public const string SecurityStampClaim = "security_stamp";

    private readonly JwtSettings _settings;

    public TokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public string CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("display_name", user.DisplayName),
            new(SecurityStampClaim, user.SecurityStamp ?? string.Empty)
        };

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(MustChangePasswordClaim, "true"));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexStringLower(bytes);
    }
}
