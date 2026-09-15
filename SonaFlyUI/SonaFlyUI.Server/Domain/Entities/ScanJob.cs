using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities;

public class ScanJob : EntityBase
{
    public Guid LibraryRootId { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Queued;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int FilesScanned { get; set; }
    public int FilesAdded { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesMissing { get; set; }

    /// <summary>
    /// Indexed tracks this scan deliberately did not reconcile because the directory that
    /// would hold them could not be read. They are neither confirmed present nor missing
    /// (backlog N13).
    /// </summary>
    public int FilesUnverified { get; set; }

    public int ErrorsCount { get; set; }
    public string? ErrorSummary { get; set; }

    public LibraryRoot LibraryRoot { get; set; } = null!;
}
