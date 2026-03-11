namespace SonaFlyUI.Server.Application.DTOs;

public record ArtistDto(Guid Id, string Name, string? SortName, Guid? ArtworkId, int AlbumCount, int TrackCount);

public record AlbumDto(Guid Id, string Title, string? ArtistName, int? Year, Guid? ArtworkId, int TrackCount);

public record AlbumDetailDto(
    Guid Id,
    string Title,
    string? ArtistName,
    Guid? ArtistId,
    int? Year,
    int? DiscCount,
    string? GenreSummary,
    Guid? ArtworkId,
    IReadOnlyList<TrackListItemDto> Tracks
);

public record TrackListItemDto(
    Guid Id,
    string Title,
    string? ArtistName,
    string? AlbumTitle,
    Guid? AlbumId,
    int? TrackNumber,
    int? DiscNumber,
    double? DurationSeconds,
    Guid? ArtworkId,
    string? Genre
);

public record GenreDto(Guid Id, string Name, int TrackCount);

public record SearchResultDto(
    IReadOnlyList<ArtistDto> Artists,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<TrackListItemDto> Tracks
);

public record SystemStatusDto(
    string Version,
    int TotalTracks,
    int TotalAlbums,
    int TotalArtists,
    int TotalGenres,
    int TotalPlaylists,
    int LibraryRootCount,
    string? CurrentScanStatus,
    DateTime? LastScanCompletedUtc
);
