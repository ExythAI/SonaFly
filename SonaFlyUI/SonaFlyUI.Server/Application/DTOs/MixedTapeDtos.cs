namespace SonaFlyUI.Server.Application.DTOs;

public record MixedTapeDto(
    Guid Id,
    string Name,
    Guid? OwnerUserId,
    string? OwnerName,
    int TargetDurationSeconds,
    double TotalDurationSeconds,
    double RemainingSeconds,
    int TrackCount,
    IReadOnlyList<MixedTapeItemDto> Items
);

public record MixedTapeItemDto(
    Guid Id,
    Guid TrackId,
    string TrackTitle,
    string? ArtistName,
    string? AlbumTitle,
    Guid? AlbumId,
    double? DurationSeconds,
    Guid? ArtworkId,
    int SortOrder
);

public record CreateMixedTapeRequest(string Name);
public record AddTrackToMixedTapeRequest(Guid TrackId);
