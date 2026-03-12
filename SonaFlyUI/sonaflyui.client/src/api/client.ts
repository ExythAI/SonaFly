import axios from 'axios';

const api = axios.create({
    baseURL: '/api',
    headers: { 'Content-Type': 'application/json' },
});

// Request interceptor: attach access token
api.interceptors.request.use((config) => {
    const token = localStorage.getItem('accessToken');
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

// Response interceptor: handle 401 with refresh token
api.interceptors.response.use(
    (response) => response,
    async (error) => {
        const originalRequest = error.config;
        if (error.response?.status === 401 && !originalRequest._retry) {
            originalRequest._retry = true;
            const refreshToken = localStorage.getItem('refreshToken');
            if (refreshToken) {
                try {
                    const res = await axios.post('/api/auth/refresh', { refreshToken });
                    localStorage.setItem('accessToken', res.data.accessToken);
                    localStorage.setItem('refreshToken', res.data.refreshToken);
                    originalRequest.headers.Authorization = `Bearer ${res.data.accessToken}`;
                    return api(originalRequest);
                } catch {
                    localStorage.removeItem('accessToken');
                    localStorage.removeItem('refreshToken');
                    window.location.href = '/login';
                }
            } else {
                window.location.href = '/login';
            }
        }
        return Promise.reject(error);
    }
);

export default api;

// ── Auth ──
export const authApi = {
    login: (username: string, password: string) =>
        api.post('/auth/login', { username, password }),
    logout: (refreshToken: string) =>
        api.post('/auth/logout', { refreshToken }),
    me: () => api.get('/auth/me'),
};

// ── Users ──
export const usersApi = {
    getAll: () => api.get('/users'),
    getById: (id: string) => api.get(`/users/${id}`),
    create: (data: any) => api.post('/users', data),
    update: (id: string, data: any) => api.put(`/users/${id}`, data),
    disable: (id: string) => api.post(`/users/${id}/disable`),
    enable: (id: string) => api.post(`/users/${id}/enable`),
    resetPassword: (id: string, newPassword: string) =>
        api.post(`/users/${id}/reset-password`, { newPassword }),
    delete: (id: string) => api.delete(`/users/${id}`),
};

// ── Library Roots ──
export const libraryRootsApi = {
    getAll: () => api.get('/library-roots'),
    create: (data: any) => api.post('/library-roots', data),
    update: (id: string, data: any) => api.put(`/library-roots/${id}`, data),
    delete: (id: string) => api.delete(`/library-roots/${id}`),
    triggerScan: (id: string, fullScan = false) => api.post(`/library-roots/${id}/scan?fullScan=${fullScan}`),
};

// ── Scans ──
export const scansApi = {
    getAll: () => api.get('/scans'),
    getCurrent: () => api.get('/scans/current'),
};

// ── Browse ──
export const browseApi = {
    artists: (page = 1, pageSize = 50) => api.get(`/artists?page=${page}&pageSize=${pageSize}`),
    artistById: (id: string) => api.get(`/artists/${id}`),
    albums: (page = 1, pageSize = 50, artistId?: string) => api.get(`/albums?page=${page}&pageSize=${pageSize}${artistId ? `&artistId=${artistId}` : ''}`),
    albumById: (id: string) => api.get(`/albums/${id}`),
    tracks: (page = 1, pageSize = 50, sortBy = 'title', sortDir = 'asc', filter = '', artistId = '') =>
        api.get(`/tracks?page=${page}&pageSize=${pageSize}&sortBy=${sortBy}&sortDir=${sortDir}${filter ? `&filter=${encodeURIComponent(filter)}` : ''}${artistId ? `&artistId=${artistId}` : ''}`),
    trackById: (id: string) => api.get(`/tracks/${id}`),
    genres: () => api.get('/genres'),
    search: (q: string, limit = 10) => api.get(`/search?q=${encodeURIComponent(q)}&limit=${limit}`),
};

// ── Playlists ──
export const playlistsApi = {
    getAll: () => api.get('/playlists'),
    getById: (id: string) => api.get(`/playlists/${id}`),
    create: (data: any) => api.post('/playlists', data),
    update: (id: string, data: any) => api.put(`/playlists/${id}`, data),
    delete: (id: string) => api.delete(`/playlists/${id}`),
    addTrack: (id: string, trackId: string) => api.post(`/playlists/${id}/items`, { trackId }),
    removeItem: (id: string, itemId: string) => api.delete(`/playlists/${id}/items/${itemId}`),
    reorder: (id: string, itemIdsInOrder: string[]) =>
        api.put(`/playlists/${id}/items/reorder`, { itemIdsInOrder }),
};

// ── Mixed Tapes ──
export const mixedTapesApi = {
    getAll: () => api.get('/mixed-tapes'),
    getById: (id: string) => api.get(`/mixed-tapes/${id}`),
    create: (data: any) => api.post('/mixed-tapes', data),
    delete: (id: string) => api.delete(`/mixed-tapes/${id}`),
    addTrack: (id: string, trackId: string) => api.post(`/mixed-tapes/${id}/items`, { trackId }),
    removeItem: (id: string, itemId: string) => api.delete(`/mixed-tapes/${id}/items/${itemId}`),
};

// ── Restrictions ──
export const restrictionsApi = {
    getForUser: (userId: string) => api.get(`/restrictions/${userId}`),
    getAll: () => api.get('/restrictions'),
    add: (userId: string, restrictionType: string, targetId: string) =>
        api.post('/restrictions', { userId, restrictionType, targetId }),
    remove: (id: string) => api.delete(`/restrictions/${id}`),
};

// ── System ──
export const systemApi = {
    health: () => api.get('/health'),
    status: () => api.get('/system/status'),
    purge: () => api.post('/system/purge'),
};

// ── Auditoriums ──
export const auditoriumsApi = {
    getAll: () => api.get('/auditoriums'),
    create: (name: string) => api.post('/auditoriums', { name }),
    delete: (id: string) => api.delete(`/auditoriums/${id}`),
};

// ── Artwork helper ──
export const artworkUrl = (artworkId: string | null | undefined) =>
    artworkId ? `/api/artwork/${artworkId}` : undefined;

// ── Stream helper ──
export const streamUrl = (trackId: string) => `/api/stream/tracks/${trackId}`;
