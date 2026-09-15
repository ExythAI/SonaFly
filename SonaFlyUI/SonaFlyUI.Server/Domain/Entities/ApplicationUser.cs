using Microsoft.AspNetCore.Identity;

namespace SonaFlyUI.Server.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastLoginUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set when the account was created with a bootstrap or administrator-assigned
    /// password. The holder may only call change-password until they clear it.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
