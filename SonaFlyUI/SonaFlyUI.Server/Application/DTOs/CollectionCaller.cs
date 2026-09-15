using System.Security.Claims;

namespace SonaFlyUI.Server.Application.DTOs;

/// <summary>
/// Who is asking, for collections that belong to somebody: playlists and mixed tapes.
///
/// Passed explicitly rather than read from an ambient accessor inside the services, so a
/// service method cannot compile without the caller having decided whose access it is.
/// </summary>
/// <param name="UserId">The authenticated user's ID.</param>
/// <param name="IsAdmin">
/// True for administrators, who may read and modify collections they do not own — they can
/// already reassign ownership through user administration, so hiding it would be a
/// formality. Restrictions still apply to them: those are per-user listening limits, not a
/// privilege level.
/// </param>
public readonly record struct CollectionCaller(Guid UserId, bool IsAdmin)
{
    /// <summary>Reads the caller out of an authenticated principal.</summary>
    /// <exception cref="InvalidOperationException">The principal carries no usable subject ID.</exception>
    public static CollectionCaller From(ClaimsPrincipal user, string adminRole)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var userId))
        {
            throw new InvalidOperationException("The authenticated principal has no user ID claim.");
        }

        return new CollectionCaller(userId, user.IsInRole(adminRole));
    }
}
