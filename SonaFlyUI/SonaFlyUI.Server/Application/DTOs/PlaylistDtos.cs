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
    int SortOrder,
    // After SortOrder so existing positional readers keep working. Needed to show a
    // cover in the player and on the lock screen for tracks played from a playlist.
    Guid? AlbumId = null,
    Guid? ArtworkId = null
);

public record CreatePlaylistRequest(string Name, string? Description, bool IsPublic = false);
public record UpdatePlaylistRequest(string? Name, string? Description, bool? IsPublic);
public record ReorderPlaylistItemsRequest(IReadOnlyList<Guid> ItemIdsInOrder);
public record AddTrackToPlaylistRequest(Guid TrackId);
