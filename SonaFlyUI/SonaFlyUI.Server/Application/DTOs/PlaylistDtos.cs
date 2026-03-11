namespace SonaFlyUI.Server.Application.DTOs;

public record PlaylistDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? OwnerUserId,
    string? OwnerName,
    bool IsPublic,
    bool IsSystemPlaylist,
    int TrackCount,
    IReadOnlyList<PlaylistItemDto> Items
);

public record PlaylistItemDto(
    Guid Id,
    Guid TrackId,
    string TrackTitle,
    string? ArtistName,
    string? AlbumTitle,
    double? DurationSeconds,
    int SortOrder
);

public record CreatePlaylistRequest(string Name, string? Description, bool IsPublic = false);
public record UpdatePlaylistRequest(string? Name, string? Description, bool? IsPublic);
public record ReorderPlaylistItemsRequest(IReadOnlyList<Guid> ItemIdsInOrder);
public record AddTrackToPlaylistRequest(Guid TrackId);
