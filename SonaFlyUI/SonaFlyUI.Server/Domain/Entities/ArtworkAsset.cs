using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities;

public class ArtworkAsset : EntityBase
{
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long FileSizeBytes { get; set; }
    public string Hash { get; set; } = string.Empty;
    public ArtworkSourceType SourceType { get; set; }
    public Guid? SourceTrackId { get; set; }
    public Guid? SourceAlbumId { get; set; }
}
