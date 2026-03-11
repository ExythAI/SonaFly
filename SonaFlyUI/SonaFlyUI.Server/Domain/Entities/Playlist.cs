namespace SonaFlyUI.Server.Domain.Entities;

public class Playlist : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? OwnerUserId { get; set; }
    public bool IsSystemPlaylist { get; set; }
    public bool IsPublic { get; set; }

    public ApplicationUser? Owner { get; set; }
    public ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
}
