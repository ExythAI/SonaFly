namespace SonaFlyUI.Server.Domain.Entities;

public abstract class AuditableEntity : EntityBase
{
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}
