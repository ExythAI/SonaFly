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
    IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(string rootPath, CancellationToken ct);
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
    Task<ScanJobDto> ScanLibraryRootAsync(Guid libraryRootId, CancellationToken ct);
}

public interface IScanQueue
{
    ValueTask EnqueueAsync(Guid libraryRootId, CancellationToken ct);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}

public interface IStreamingService
{
    Task<StreamableTrackResult?> GetStreamableTrackAsync(Guid trackId, CancellationToken ct);
}

public interface IPlaylistService
{
    Task<Guid> CreateAsync(CreatePlaylistRequest request, Guid ownerUserId, CancellationToken ct);
    Task UpdateAsync(Guid playlistId, UpdatePlaylistRequest request, CancellationToken ct);
    Task DeleteAsync(Guid playlistId, CancellationToken ct);
    Task<PlaylistDto?> GetByIdAsync(Guid playlistId, CancellationToken ct);
    Task<IReadOnlyList<PlaylistDto>> GetAllAsync(Guid? ownerUserId, CancellationToken ct);
    Task AddTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct);
    Task RemoveItemAsync(Guid playlistId, Guid itemId, CancellationToken ct);
    Task ReorderAsync(Guid playlistId, ReorderPlaylistItemsRequest request, CancellationToken ct);
}

public interface IMixedTapeService
{
    Task<Guid> CreateAsync(CreateMixedTapeRequest request, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<MixedTapeDto>> GetAllAsync(Guid ownerUserId, CancellationToken ct);
    Task<MixedTapeDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task AddTrackAsync(Guid mixedTapeId, Guid trackId, CancellationToken ct);
    Task RemoveItemAsync(Guid mixedTapeId, Guid itemId, CancellationToken ct);
}
