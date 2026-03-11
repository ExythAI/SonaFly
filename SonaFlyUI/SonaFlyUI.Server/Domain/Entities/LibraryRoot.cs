using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities;

public class LibraryRoot : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsReadOnly { get; set; } = true;
    public DateTime? LastScanStartedUtc { get; set; }
    public DateTime? LastScanCompletedUtc { get; set; }
    public ScanStatus? LastScanStatus { get; set; }
    public string? LastScanError { get; set; }

    public ICollection<Track> Tracks { get; set; } = new List<Track>();
    public ICollection<ScanJob> ScanJobs { get; set; } = new List<ScanJob>();
}
