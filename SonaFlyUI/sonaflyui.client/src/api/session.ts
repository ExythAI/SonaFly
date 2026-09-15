/**
 * Where the browser's credentials live.
 *
 * The refresh token is never here: it exists only as an HttpOnly cookie the server sets,
 * which script — including script injected into this page — cannot read. The access
 * token is deliberately kept in a module variable rather than localStorage, so it dies
 * with the tab and leaves nothing on disk for a later reader to find.
 */

const LEGACY_ACCESS_KEY = 'accessToken';
const LEGACY_REFRESH_KEY = 'refreshToken';

/** Sent on auth calls that authenticate with the cookie; see RefreshTokenCookie.cs. */
export const CLIENT_HEADER = 'X-SonaFly-Client';
export const CLIENT_HEADER_VALUE = 'web';

let accessToken: string | null = null;

/**
 * Incremented whenever the session is deliberately replaced (login) or ended (logout),
 * so a refresh that was already in flight cannot write its result over the new state.
 */
let epoch = 0;

export const getAccessToken = (): string | null => accessToken;

export const setAccessToken = (token: string | null): void => { accessToken = token; };

export const currentEpoch = (): number => epoch;

/** Starts a new session generation and drops any token from the previous one. */
export const resetSession = (): number => {
    accessToken = null;
    return ++epoch;
};

/** localStorage may be unavailable (private mode, blocked site data); never throw for it. */
const readLegacy = (key: string): string | null => {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
};

const removeLegacy = (key: string): void => {
    try {
        localStorage.removeItem(key);
    } catch {
        /* nothing we can do, and nothing depends on it */
    }
};

/**
 * Earlier versions kept both tokens in localStorage. Read the refresh token once so the
 * session can be traded for a cookie, then remove both copies whatever the outcome —
 * leaving a stale seven-day credential on disk is the problem being fixed.
 */
export const takeLegacyRefreshToken = (): string | null => {
    const token = readLegacy(LEGACY_REFRESH_KEY);
    removeLegacy(LEGACY_REFRESH_KEY);
    removeLegacy(LEGACY_ACCESS_KEY);
    return token;
};
