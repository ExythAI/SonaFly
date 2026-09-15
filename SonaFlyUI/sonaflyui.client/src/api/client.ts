import axios from 'axios';
import {
    CLIENT_HEADER, CLIENT_HEADER_VALUE,
    currentEpoch, getAccessToken, resetSession, setAccessToken, takeLegacyRefreshToken,
} from './session.ts';
import type {
    AlbumDetailDto, AlbumDto, ArtistDto, AuditoriumListDto, CreateUserRequest, GenreDto,
    LibraryRootDto, MixedTapeDto, PaginatedResult, PlaylistDto, ScanJobDto, SearchResultDto,
    SystemStatusDto, TrackListItemDto, UserInfoDto, UserRestrictionDto,
} from './types.ts';

const api = axios.create({
    baseURL: '/api',
    headers: { 'Content-Type': 'application/json' },
    // The refresh cookie is same-origin, but be explicit so a proxied or split-origin
    // deployment behaves the same way.
    withCredentials: true,
});

// Request interceptor: attach the in-memory access token
api.interceptors.request.use((config) => {
    const token = getAccessToken();
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    config.headers[CLIENT_HEADER] = CLIENT_HEADER_VALUE;
    return config;
});

const refreshConfig = {
    withCredentials: true,
    headers: { [CLIENT_HEADER]: CLIENT_HEADER_VALUE },
};

/**
 * Serialises everything that changes the refresh cookie — rotation, sign-in, sign-out.
 *
 * The cookie rotates on every use, and the server treats a token presented twice as
 * evidence that a copy is loose and revokes the whole chain. So two overlapping rotations
 * do not merely waste a request; they end the session. That can happen without anything
 * unusual going on: React StrictMode mounts the app twice in development, and a second tab
 * shares the very same cookie.
 *
 * Two layers, because neither is sufficient alone. The Web Lock covers other tabs but is
 * not everywhere; the local queue covers this tab and is the fallback where it is missing.
 */
const SESSION_LOCK = 'sonafly-session';
let sessionQueue: Promise<unknown> = Promise.resolve();

const withSessionLock = <T>(operation: () => Promise<T>): Promise<T> => {
    const run = () => {
        const locks = globalThis.navigator?.locks;
        // The lib.dom signature describes a callback returning T, not a promise of one,
        // so an async callback needs the cast; the runtime awaits it either way.
        return locks
            ? (locks.request(SESSION_LOCK, operation as () => never) as Promise<T>)
            : operation();
    };

    // Run after whatever is queued, whether that succeeded or failed.
    const result = sessionQueue.then(run, run);
    sessionQueue = result.then(() => undefined, () => undefined);
    return result;
};

/**
 * Trades the refresh cookie for a new access token, one rotation at a time.
 *
 * Callers that arrive while a rotation is in flight join it rather than starting another:
 * a cold load in StrictMode calls this twice, and so does every request that was holding
 * the same expired access token.
 *
 * @returns the new access token, or null when the session was replaced or ended while this
 * was in flight — a result from a session that no longer exists must not be installed.
 * @throws the underlying error when the rotation itself failed.
 */
let pendingRotation: Promise<string | null> | null = null;
const rotateSession = (): Promise<string | null> => {
    if (!pendingRotation) {
        const epoch = currentEpoch();
        pendingRotation = withSessionLock(async () => {
            // Read the legacy token inside the lock: a caller that joined an in-flight
            // rotation would otherwise spend it and discard the result.
            const legacyRefreshToken = takeLegacyRefreshToken();
            const body = legacyRefreshToken
                ? { refreshToken: legacyRefreshToken, useCookie: true }
                : {};

            const res = await axios.post('/api/auth/refresh', body, refreshConfig);

            // Do not restore a session that was logged out or replaced while refreshing.
            if (epoch !== currentEpoch()) return null;

            setAccessToken(res.data.accessToken);
            return res.data.accessToken as string;
        }).finally(() => { pendingRotation = null; });
    }
    return pendingRotation;
};

const refreshAccessToken = async (): Promise<string> => {
    const token = await rotateSession();
    if (token === null) throw new Error('Session changed');
    return token;
};

/**
 * Whether the server has told us there is no session, as opposed to not having been able to
 * tell us anything. A refused credential is final; an outage, a rate limit or an unreachable
 * network is not, and must not be reported as a signed-out user.
 */
const isDefinitivelySignedOut = (error: unknown): boolean => {
    if (!axios.isAxiosError(error)) return false;
    const status = error.response?.status;
    return status === 400 || status === 401 || status === 403;
};

/**
 * Re-establishes the session on a cold page load, when the access token (memory only)
 * is gone but the refresh cookie may still be valid.
 *
 * A session left over from the versions that stored both tokens in localStorage is
 * upgraded here: the old refresh token is spent once to obtain a cookie, and the
 * plaintext copies are deleted.
 *
 * @returns the new access token, or null when there is definitively no usable session.
 * @throws when the answer is simply unknown — an outage, a 429 or 503, a dropped
 * connection. The caller offers a retry instead of sending the user to the login page,
 * which would look like being signed out and would strand a perfectly good session.
 */
export const restoreSession = async (): Promise<string | null> => {
    try {
        return await rotateSession();
    } catch (error) {
        if (isDefinitivelySignedOut(error)) return null;
        throw error;
    }
};

api.interceptors.response.use(
    (response) => response,
    async (error) => {
        const originalRequest = error.config;
        if (error.response?.status !== 401 || !originalRequest || originalRequest._retry ||
            ['/auth/login', '/auth/refresh', '/auth/logout'].includes(originalRequest.url)) return Promise.reject(error);
        originalRequest._retry = true;
        const failedAuthorization = originalRequest.headers.Authorization;
        const currentToken = getAccessToken();
        const epoch = currentEpoch();
        try {
            // Another request may already have refreshed before this 401 arrived.
            const token = currentToken && failedAuthorization !== `Bearer ${currentToken}`
                ? currentToken : await refreshAccessToken();
            originalRequest.headers.Authorization = `Bearer ${token}`;
            return api(originalRequest);
        } catch (refreshError) {
            // Only tear down the session this request belonged to.
            if (epoch === currentEpoch()) {
                resetSession();
                window.location.href = '/login';
            }
            return Promise.reject(refreshError);
        }
    }
);

export default api;

// ── Auth ──
// Sign-in and sign-out replace and delete the refresh cookie, so they queue behind any
// rotation still in flight rather than racing it — a logout overtaken by a rotation would
// leave the browser holding a cookie the user believes they have given up.
export const authApi = {
    login: (username: string, password: string) =>
        withSessionLock(() => api.post('/auth/login', { username, password, useCookie: true })),
    // The refresh token is in the HttpOnly cookie; the server clears it.
    logout: () => withSessionLock(() => api.post('/auth/logout', {})),
    me: () => api.get<UserInfoDto>('/auth/me'),
    changePassword: (currentPassword: string, newPassword: string) =>
        api.post('/auth/change-password', { currentPassword, newPassword }),
};

// ── Users ──
export const usersApi = {
    getAll: () => api.get<UserInfoDto[]>('/users'),
    getById: (id: string) => api.get<UserInfoDto>(`/users/${id}`),
    create: (data: CreateUserRequest) => api.post<UserInfoDto>('/users', data),
    update: (id: string, data: Partial<Pick<UserInfoDto, 'email' | 'displayName'>> & { role?: string }) =>
        api.put(`/users/${id}`, data),
    disable: (id: string) => api.post(`/users/${id}/disable`),
    enable: (id: string) => api.post(`/users/${id}/enable`),
    resetPassword: (id: string, newPassword: string) =>
        api.post(`/users/${id}/reset-password`, { newPassword }),
    delete: (id: string) => api.delete(`/users/${id}`),
};

// ── Library Roots ──
type LibraryRootInput = { name?: string; path?: string; isEnabled?: boolean; isReadOnly?: boolean };

export const libraryRootsApi = {
    getAll: () => api.get<LibraryRootDto[]>('/library-roots'),
    create: (data: LibraryRootInput) => api.post<LibraryRootDto>('/library-roots', data),
    update: (id: string, data: LibraryRootInput) => api.put(`/library-roots/${id}`, data),
    delete: (id: string) => api.delete(`/library-roots/${id}`),
    triggerScan: (id: string, fullScan = false) => api.post(`/library-roots/${id}/scan?fullScan=${fullScan}`),
};

// ── Scans ──
export const scansApi = {
    getAll: () => api.get<ScanJobDto[]>('/scans'),
    getCurrent: () => api.get<ScanJobDto | null>('/scans/current'),
};

// ── Browse ──
export const browseApi = {
    artists: (page = 1, pageSize = 50) => api.get<PaginatedResult<ArtistDto>>(`/artists?page=${page}&pageSize=${pageSize}`),
    artistById: (id: string) => api.get<ArtistDto>(`/artists/${id}`),
    albums: (page = 1, pageSize = 50, artistId?: string) => api.get<PaginatedResult<AlbumDto>>(`/albums?page=${page}&pageSize=${pageSize}${artistId ? `&artistId=${artistId}` : ''}`),
    albumById: (id: string) => api.get<AlbumDetailDto>(`/albums/${id}`),
    tracks: (page = 1, pageSize = 50, sortBy = 'title', sortDir = 'asc', filter = '', artistId = '') =>
        api.get<PaginatedResult<TrackListItemDto>>(`/tracks?page=${page}&pageSize=${pageSize}&sortBy=${sortBy}&sortDir=${sortDir}${filter ? `&filter=${encodeURIComponent(filter)}` : ''}${artistId ? `&artistId=${artistId}` : ''}`),
    trackById: (id: string) => api.get<TrackListItemDto>(`/tracks/${id}`),
    genres: () => api.get<GenreDto[]>('/genres'),
    search: (q: string, limit = 10) => api.get<SearchResultDto>(`/search?q=${encodeURIComponent(q)}&limit=${limit}`),
};

// ── Playlists ──
export const playlistsApi = {
    getAll: () => api.get<PlaylistDto[]>('/playlists'),
    getById: (id: string) => api.get<PlaylistDto>(`/playlists/${id}`),
    create: (data: { name: string; description?: string | null; isPublic?: boolean }) =>
        api.post<PlaylistDto>('/playlists', data),
    update: (id: string, data: { name?: string; description?: string | null; isPublic?: boolean }) =>
        api.put(`/playlists/${id}`, data),
    delete: (id: string) => api.delete(`/playlists/${id}`),
    addTrack: (id: string, trackId: string) => api.post(`/playlists/${id}/items`, { trackId }),
    removeItem: (id: string, itemId: string) => api.delete(`/playlists/${id}/items/${itemId}`),
    reorder: (id: string, itemIdsInOrder: string[]) =>
        api.put(`/playlists/${id}/items/reorder`, { itemIdsInOrder }),
};

// ── Mixed Tapes ──
export const mixedTapesApi = {
    getAll: () => api.get<MixedTapeDto[]>('/mixed-tapes'),
    getById: (id: string) => api.get<MixedTapeDto>(`/mixed-tapes/${id}`),
    create: (data: { name: string }) => api.post<MixedTapeDto>('/mixed-tapes', data),
    delete: (id: string) => api.delete(`/mixed-tapes/${id}`),
    addTrack: (id: string, trackId: string) => api.post(`/mixed-tapes/${id}/items`, { trackId }),
    removeItem: (id: string, itemId: string) => api.delete(`/mixed-tapes/${id}/items/${itemId}`),
};

// ── Restrictions ──
export const restrictionsApi = {
    getForUser: (userId: string) => api.get<UserRestrictionDto[]>(`/restrictions/${userId}`),
    getAll: () => api.get<UserRestrictionDto[]>('/restrictions'),
    add: (userId: string, restrictionType: string, targetId: string) =>
        api.post('/restrictions', { userId, restrictionType, targetId }),
    remove: (id: string) => api.delete(`/restrictions/${id}`),
};

// ── System ──
export const systemApi = {
    health: () => api.get('/health'),
    status: () => api.get<SystemStatusDto>('/system/status'),
    purge: () => api.post('/system/purge'),
};

// ── Auditoriums ──
export const auditoriumsApi = {
    getAll: () => api.get<AuditoriumListDto[]>('/auditoriums'),
    create: (name: string) => api.post<AuditoriumListDto>('/auditoriums', { name }),
    delete: (id: string) => api.delete(`/auditoriums/${id}`),
};

// ── Artwork helper ──
export const artworkUrl = (artworkId: string | null | undefined) =>
    artworkId ? `/api/artwork/${artworkId}` : undefined;

// ── Stream helper ──
export const streamUrl = async (trackId: string): Promise<string> => {
    const response = await api.get<{ url: string }>(`/stream/tracks/${trackId}/url`);
    return response.data.url;
};
