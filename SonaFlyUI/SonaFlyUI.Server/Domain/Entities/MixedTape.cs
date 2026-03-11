namespace SonaFlyUI.Server.Domain.Entities;

public class MixedTape : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? OwnerUserId { get; set; }
    public int TargetDurationSeconds { get; set; } = 3600; // 60 minutes

    public ApplicationUser? Owner { get; set; }
    public ICollection<MixedTapeItem> Items { get; set; } = new List<MixedTapeItem>();
}
