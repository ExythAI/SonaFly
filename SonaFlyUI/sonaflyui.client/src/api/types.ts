/**
 * The API's response shapes.
 *
 * These mirror the records in SonaFlyUI.Server/Application/DTOs, camel-cased by the
 * JSON serializer. They are hand-maintained: when a server DTO changes, change it here
 * too — the point is that a mismatch shows up as a type error in the page that uses the
 * field, rather than as `undefined` at runtime, which is what `any` gave us.
 *
 * `Guid` and `DateTime` both arrive as strings over JSON; the aliases keep the intent
 * readable at the use site.
 */

export type Guid = string;
export type IsoDateTime = string;

// ── Browse ──

export interface ArtistDto {
    id: Guid;
    name: string;
    sortName: string | null;
    artworkId: Guid | null;
    albumCount: number;
    trackCount: number;
}

export interface AlbumDto {
    id: Guid;
    title: string;
    artistName: string | null;
    year: number | null;
    artworkId: Guid | null;
    trackCount: number;
}

export interface TrackListItemDto {
    id: Guid;
    title: string;
    artistName: string | null;
    albumTitle: string | null;
    albumId: Guid | null;
    trackNumber: number | null;
    discNumber: number | null;
    durationSeconds: number | null;
    artworkId: Guid | null;
    genre: string | null;
}

export interface AlbumDetailDto {
    id: Guid;
    title: string;
    artistName: string | null;
    artistId: Guid | null;
    year: number | null;
    discCount: number | null;
    genreSummary: string | null;
    artworkId: Guid | null;
    tracks: TrackListItemDto[];
}

export interface GenreDto {
    id: Guid;
    name: string;
    trackCount: number;
}

export interface SearchResultDto {
    artists: ArtistDto[];
    albums: AlbumDto[];
    tracks: TrackListItemDto[];
}

export interface PaginatedResult<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    hasPreviousPage: boolean;
    hasNextPage: boolean;
}

// ── Users ──

export interface UserInfoDto {
    id: Guid;
    userName: string;
    email: string;
    displayName: string;
    isEnabled: boolean;
    roles: string[];
    lastLoginUtc: IsoDateTime | null;
    createdUtc: IsoDateTime;
    mustChangePassword: boolean;
}

export interface CreateUserRequest {
    userName: string;
    email: string;
    displayName: string;
    password: string;
    role: string;
}

// ── Playlists ──

export interface PlaylistItemDto {
    id: Guid;
    trackId: Guid;
    trackTitle: string;
    artistName: string | null;
    albumTitle: string | null;
    durationSeconds: number | null;
    sortOrder: number;
}

export interface PlaylistDto {
    id: Guid;
    name: string;
    description: string | null;
    ownerUserId: Guid | null;
    ownerName: string | null;
    isPublic: boolean;
    isSystemPlaylist: boolean;
    trackCount: number;
    items: PlaylistItemDto[];
}

// ── Mixed tapes ──

export interface MixedTapeItemDto {
    id: Guid;
    trackId: Guid;
    trackTitle: string;
    artistName: string | null;
    albumTitle: string | null;
    albumId: Guid | null;
    durationSeconds: number | null;
    artworkId: Guid | null;
    sortOrder: number;
}

export interface MixedTapeDto {
    id: Guid;
    name: string;
    ownerUserId: Guid | null;
    ownerName: string | null;
    targetDurationSeconds: number;
    totalDurationSeconds: number;
    remainingSeconds: number;
    trackCount: number;
    items: MixedTapeItemDto[];
}

// ── Library ──

export interface LibraryRootDto {
    id: Guid;
    name: string;
    path: string;
    isEnabled: boolean;
    isReadOnly: boolean;
    lastScanStartedUtc: IsoDateTime | null;
    lastScanCompletedUtc: IsoDateTime | null;
    lastScanStatus: string | null;
    lastScanError: string | null;
}

export interface ScanJobDto {
    id: Guid;
    libraryRootId: Guid;
    libraryRootName: string | null;
    status: string;
    startedUtc: IsoDateTime | null;
    completedUtc: IsoDateTime | null;
    filesScanned: number;
    filesAdded: number;
    filesUpdated: number;
    filesMissing: number;
    /** Indexed tracks a partial scan deliberately left alone because their folder was unreadable. */
    filesUnverified: number;
    errorsCount: number;
    errorSummary: string | null;
}

// ── System ──

export interface SystemStatusDto {
    version: string;
    totalTracks: number;
    totalAlbums: number;
    totalArtists: number;
    totalGenres: number;
    totalPlaylists: number;
    libraryRootCount: number;
    currentScanStatus: string | null;
    lastScanCompletedUtc: IsoDateTime | null;
}

// ── Restrictions ──

export type RestrictionType = 'Album' | 'Artist' | 'Genre';

export interface UserRestrictionDto {
    id: Guid;
    userId: Guid;
    restrictionType: RestrictionType;
    targetId: Guid;
    targetName: string | null;
}

// ── Auditoriums ──
//
// The live room shapes (Room, RoomState, RoomQueueItem) live in components/AuditoriumContext,
// next to the SignalR code that produces them. Only the REST list shape belongs here.

export interface AuditoriumListDto {
    id: Guid;
    name: string;
    createdByUserId: Guid;
    activeUserCount: number;
    nowPlaying: string | null;
}

// ── Music identification ──

export interface IdentificationStatusDto {
    enabled: boolean;
    acoustIdConfigured: boolean;
    acoustIdSource: string;
    fpcalcConfigured: boolean;
    ffmpegConfigured: boolean;
    localOnlyMode: boolean;
    blockers: string[];
    message: string;
}

export interface IdentificationKeyStateDto {
    configured: boolean;
    source: string;
}

export interface IdentificationJobDto {
    id: string;
    libraryRootId: string;
    libraryRootName: string | null;
    status: 'Queued' | 'Running' | 'WaitingForNetwork' | 'Paused' | 'Completed' | 'CompletedWithErrors' | 'Cancelled' | 'Failed';
    stage: string | null;
    startedUtc: string | null;
    finishedUtc: string | null;
    totalItems: number;
    hashedCount: number;
    fingerprintedCount: number;
    lookedUpCount: number;
    resolvedCount: number;
    ambiguousCount: number;
    errorCount: number;
    proposalsReadyCount: number;
    errorSummary: string | null;
}

export interface IdentificationJobQueuedDto {
    jobId: string | null;
    status: string;
    message: string;
}
