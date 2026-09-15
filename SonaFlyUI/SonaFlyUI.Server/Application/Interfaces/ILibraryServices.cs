using SonaFlyUI.Server.Application.DTOs;

namespace SonaFlyUI.Server.Application.Interfaces;

public interface ILibraryRootService
{
    Task<IReadOnlyList<LibraryRootDto>> GetAllAsync(CancellationToken ct);
    Task<LibraryRootDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateAsync(CreateLibraryRootRequest request, CancellationToken ct);
    Task UpdateAsync(Guid id, UpdateLibraryRootRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IFileScanner
{
    /// <summary>
    /// Enumerates audio files beneath <paramref name="rootPath"/>, recording in
    /// <paramref name="report"/> which directories were positively enumerated and which
    /// were unreachable. Callers must not treat an unseen file as deleted unless the
    /// report vouches for its directory.
    /// </summary>
    IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(
        string rootPath, ScanTraversalReport report, CancellationToken ct);
}

public interface IMetadataReader
{
    Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct);
}

public interface IArtworkService
{
    Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, string filePath, CancellationToken ct);
    Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct);
}

public interface ILibraryIndexService
{
    Task<ScanJobDto> ScanLibraryRootAsync(ScanRequest request, CancellationToken ct);
}

/// <summary>
/// A queued scan. <paramref name="ScanJobId"/> names the ScanJob row that was persisted before
/// the request was accepted, so the job is visible (and recoverable) even if the process
/// restarts before the worker picks it up (backlog N17).
/// </summary>
public record ScanRequest(Guid LibraryRootId, bool FullScan, Guid ScanJobId);

public interface IScanQueue
{
    /// <summary>
    /// Hands a request to the worker. Returns false when the in-memory queue is saturated; the
    /// caller is responsible for the persisted job in that case.
    /// </summary>
    bool TryEnqueue(ScanRequest request);

    ValueTask<ScanRequest> DequeueAsync(CancellationToken ct);
}

public interface IStreamingService
{
    Task<StreamableTrackResult?> GetStreamableTrackAsync(Guid trackId, Guid userId, CancellationToken ct);
}

/// <summary>
/// Playlists belong to a user. Every method takes the <see cref="CollectionCaller"/> making
/// the request and enforces access itself — reading is owner, public, system or admin;
/// writing is owner or admin only, so a playlist being public never makes it editable.
/// Returned items and counts are filtered by the caller's restrictions.
/// </summary>
public interface IPlaylistService
{
    Task<Guid> CreateAsync(CreatePlaylistRequest request, Guid ownerUserId, CancellationToken ct);
    Task UpdateAsync(Guid playlistId, UpdatePlaylistRequest request, CollectionCaller caller, CancellationToken ct);
    Task DeleteAsync(Guid playlistId, CollectionCaller caller, CancellationToken ct);
    Task<PlaylistDto?> GetByIdAsync(Guid playlistId, CollectionCaller caller, CancellationToken ct);
    Task<IReadOnlyList<PlaylistDto>> GetAllAsync(CollectionCaller caller, CancellationToken ct);
    Task AddTrackAsync(Guid playlistId, Guid trackId, CollectionCaller caller, CancellationToken ct);
    Task RemoveItemAsync(Guid playlistId, Guid itemId, CollectionCaller caller, CancellationToken ct);
    Task ReorderAsync(Guid playlistId, ReorderPlaylistItemsRequest request, CollectionCaller caller, CancellationToken ct);
}

/// <summary>
/// Mixed tapes are private to their owner (admins excepted); the same access rules as
/// <see cref="IPlaylistService"/> otherwise apply.
/// </summary>
public interface IMixedTapeService
{
    Task<Guid> CreateAsync(CreateMixedTapeRequest request, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<MixedTapeDto>> GetAllAsync(CollectionCaller caller, CancellationToken ct);
    Task<MixedTapeDto?> GetByIdAsync(Guid id, CollectionCaller caller, CancellationToken ct);
    Task DeleteAsync(Guid id, CollectionCaller caller, CancellationToken ct);
    Task AddTrackAsync(Guid mixedTapeId, Guid trackId, CollectionCaller caller, CancellationToken ct);
    Task RemoveItemAsync(Guid mixedTapeId, Guid itemId, CollectionCaller caller, CancellationToken ct);
}
