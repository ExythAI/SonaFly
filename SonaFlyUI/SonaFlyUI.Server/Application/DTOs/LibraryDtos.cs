namespace SonaFlyUI.Server.Application.DTOs;

public record LibraryRootDto(
    Guid Id,
    string Name,
    string Path,
    bool IsEnabled,
    bool IsReadOnly,
    DateTime? LastScanStartedUtc,
    DateTime? LastScanCompletedUtc,
    string? LastScanStatus,
    string? LastScanError
);

public record CreateLibraryRootRequest(string Name, string Path, bool IsReadOnly = true);
public record UpdateLibraryRootRequest(string? Name, string? Path, bool? IsEnabled, bool? IsReadOnly);

public record ScanJobDto(
    Guid Id,
    Guid LibraryRootId,
    string? LibraryRootName,
    string Status,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    int FilesScanned,
    int FilesAdded,
    int FilesUpdated,
    int FilesMissing,
    int ErrorsCount,
    string? ErrorSummary
);

public record DiscoveredAudioFile(
    string FilePath,
    string FileName,
    string Extension,
    long FileSizeBytes,
    DateTime LastModifiedUtc
);

public record AudioMetadata
{
    public string? Title { get; init; }
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public int? TrackNumber { get; init; }
    public int? DiscNumber { get; init; }
    public string? Genre { get; init; }
    public int? Year { get; init; }
    public double? DurationSeconds { get; init; }
    public int? BitRateKbps { get; init; }
    public int? SampleRateHz { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public byte[]? ArtworkData { get; init; }
    public string? ArtworkMimeType { get; init; }
}

public record ArtworkResult(Guid ArtworkId, string StoragePath);

public record FileStreamResultModel(Stream Stream, string MimeType, string FileName, long FileSize);

public record StreamableTrackResult(
    string FilePath,
    string MimeType,
    string FileName,
    long FileSizeBytes
);
