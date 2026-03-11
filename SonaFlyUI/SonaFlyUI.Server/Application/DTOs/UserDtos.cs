namespace SonaFlyUI.Server.Application.DTOs;

public record UserInfoDto(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    bool IsEnabled,
    IEnumerable<string> Roles,
    DateTime? LastLoginUtc,
    DateTime CreatedUtc
);

public record CreateUserRequest(
    string UserName,
    string Email,
    string DisplayName,
    string Password,
    string Role
);

public record UpdateUserRequest(
    string? Email,
    string? DisplayName,
    string? Role
);

public record ResetPasswordRequest(string NewPassword);
