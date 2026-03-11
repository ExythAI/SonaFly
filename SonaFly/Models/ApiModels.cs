namespace SonaFly.Models;

// ── Auth ──
public record LoginRequest(string Username, string Password);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresUtc,
    UserInfo User
);

public record RefreshResponse(string AccessToken, string RefreshToken, DateTime ExpiresUtc);

public record UserInfo(
    Guid Id, string UserName, string Email, string DisplayName,
    bool IsEnabled, IEnumerable<string> Roles, DateTime? LastLoginUtc, DateTime CreatedUtc
);

// ── Browse ──
public record PaginatedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);

public record ArtistDto(Guid Id, string Name, string? SortName, Guid? ArtworkId, int AlbumCount, int TrackCount);

public record AlbumDto(Guid Id, string Title, string? ArtistName, int? Year, Guid? ArtworkId, int TrackCount);

public record AlbumDetailDto(
    Guid Id, string Title, string? ArtistName, Guid? ArtistId,
    int? Year, Guid? ArtworkId, int TrackCount,
    IReadOnlyList<TrackDto> Tracks
);

public record TrackDto(
    Guid Id, string Title, string? ArtistName, string? AlbumTitle,
    Guid? AlbumId, int? TrackNumber, int? DiscNumber,
    double? DurationSeconds, Guid? ArtworkId, string? Genre
);

public record GenreDto(Guid Id, string Name, int TrackCount);

public record SearchResultDto(
    IReadOnlyList<ArtistDto> Artists,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<TrackDto> Tracks
);

// ── Playlists ──
public record PlaylistDto(
    Guid Id, string Name, string? Description, Guid? OwnerUserId,
    string? OwnerName, bool IsPublic, bool IsSystemPlaylist,
    int TrackCount, IReadOnlyList<PlaylistItemDto> Items
);

public record PlaylistItemDto(
    Guid Id, Guid TrackId, string TrackTitle, string? ArtistName,
    string? AlbumTitle, double? DurationSeconds, int SortOrder
);

// ── Mixed Tapes ──
public record MixedTapeDto(
    Guid Id, string Name, Guid? OwnerUserId, string? OwnerName,
    int TargetDurationSeconds, double TotalDurationSeconds, double RemainingSeconds,
    int TrackCount, IReadOnlyList<MixedTapeItemDto> Items
);

public record MixedTapeItemDto(
    Guid Id, Guid TrackId, string TrackTitle, string? ArtistName,
    string? AlbumTitle, Guid? AlbumId, double? DurationSeconds,
    Guid? ArtworkId, int SortOrder
);

// ── Auditorium ──
public record AuditoriumDto(Guid Id, string Name, Guid CreatedByUserId, int ActiveUserCount, string? NowPlaying);

public record AuditoriumStateDto(
    Guid AuditoriumId,
    Guid? CurrentTrackId, string? CurrentTrackTitle, string? CurrentArtistName, Guid? CurrentArtworkId,
    double? CurrentTrackDuration, double CurrentPositionSeconds, bool IsPaused,
    Guid? StartedByUserId, string? StartedByUserName,
    List<QueueItemDto> Queue,
    List<ActiveUserDto> ActiveUsers,
    DateTime ServerUtcNow
);

public record QueueItemDto(Guid Id, Guid TrackId, string Title, string? ArtistName, Guid? ArtworkId, Guid QueuedByUserId, string QueuedByUserName);
public record ActiveUserDto(Guid UserId, string DisplayName);
