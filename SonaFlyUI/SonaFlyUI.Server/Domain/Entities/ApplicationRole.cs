using Microsoft.AspNetCore.Identity;

namespace SonaFlyUI.Server.Domain.Entities;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string roleName) : base(roleName) { }
}
