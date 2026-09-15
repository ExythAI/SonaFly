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
    bool IsEnabled, IEnumerable<string> Roles, DateTime? LastLoginUtc, DateTime CreatedUtc,
    // True while the account still holds a password an administrator chose, or the one
    // generated when the server bootstrapped. The server refuses everything except the
    // change itself until it is replaced, so the app must route there rather than into
    // the library.
    bool MustChangePassword = false
);

/// <summary>
/// Credentials issued to replace the ones a password change just revoked.
/// </summary>
/// <remarks>
/// Changing the password ends every session, including the one that asked for the change.
/// The replacement pair is the only way to stay signed in; discarding it leaves the app
/// holding tokens the server has already thrown away.
/// </remarks>
public record ChangePasswordResponse(string AccessToken, string? RefreshToken, DateTime ExpiresUtc);

/// <summary>What happened to a password change, and to the session that asked for it.</summary>
public enum ChangePasswordResult
{
    /// <summary>Changed, and the app holds the replacement credentials.</summary>
    Changed,

    /// <summary>Refused — most often the current password was wrong.</summary>
    Rejected,

    /// <summary>
    /// Changed, but the replacement credentials could not be kept: the user switched
    /// server or signed out while the request was in flight, or the server did not return
    /// them. The password is the new one; this device has to sign in again.
    /// </summary>
    ChangedButSignedOut
}

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
