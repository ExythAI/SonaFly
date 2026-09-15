import { act, cleanup, render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { AuthProvider, useAuth } from './AuthContext';
import { authApi, restoreSession } from '../api/client';
import { getAccessToken, setAccessToken } from '../api/session';

vi.mock('../api/client', () => ({
    authApi: { me: vi.fn(), login: vi.fn(), logout: vi.fn(), changePassword: vi.fn() },
    restoreSession: vi.fn(),
}));

let auth: ReturnType<typeof useAuth>;
let client: QueryClient;
function Probe() { auth = useAuth(); return null; }
function mount() { render(<QueryClientProvider client={client}><AuthProvider><Probe /></AuthProvider></QueryClientProvider>); }

beforeEach(() => {
    vi.mocked(authApi.me).mockReset();
    vi.mocked(restoreSession).mockReset();
    // A cold load has no access token in memory; it comes back from the refresh cookie.
    vi.mocked(restoreSession).mockImplementation(async () => {
        setAccessToken('restored-session');
        return 'restored-session';
    });
    client = new QueryClient();
});

afterEach(() => { cleanup(); client.clear(); setAccessToken(null); localStorage.clear(); });

test('temporary server failures preserve credentials and allow retry', async () => {
    vi.mocked(authApi.me).mockRejectedValueOnce(new Error('Server unavailable'));
    mount();
    await waitFor(() => expect(auth.loading).toBe(false));
    expect(auth.sessionError).toBe('Server unavailable');
    expect(getAccessToken()).toBe('restored-session');
    vi.mocked(authApi.me).mockResolvedValueOnce({ data: { id: 'listener', roles: ['User'] } } as Awaited<ReturnType<typeof authApi.me>>);
    act(() => auth.retrySession());
    await waitFor(() => expect(auth.isAuthenticated).toBe(true));
    expect(auth.sessionError).toBeNull();
});

test('an outage during restoration offers a retry rather than signing the user out', async () => {
    // restoreSession answers null only for a credential the server actually refused. A 503
    // means we do not know, and treating "do not know" as "signed out" strands a healthy
    // session at the login page.
    vi.mocked(restoreSession).mockRejectedValueOnce(new Error('Service Unavailable'));
    mount();
    await waitFor(() => expect(auth.loading).toBe(false));

    expect(auth.sessionError).toBe('Service Unavailable');
    expect(auth.isAuthenticated).toBe(false);
    expect(authApi.me).not.toHaveBeenCalled();

    vi.mocked(restoreSession).mockImplementationOnce(async () => {
        setAccessToken('restored-session');
        return 'restored-session';
    });
    vi.mocked(authApi.me).mockResolvedValueOnce({ data: { id: 'listener', roles: ['User'] } } as Awaited<ReturnType<typeof authApi.me>>);
    act(() => auth.retrySession());

    await waitFor(() => expect(auth.isAuthenticated).toBe(true));
    expect(auth.sessionError).toBeNull();
});

test('no restorable session leaves the app unauthenticated', async () => {
    vi.mocked(restoreSession).mockResolvedValueOnce(null);
    mount();
    await waitFor(() => expect(auth.loading).toBe(false));
    expect(auth.isAuthenticated).toBe(false);
    expect(authApi.me).not.toHaveBeenCalled();
});

test('logout clears user-specific cached server data and the in-memory token', async () => {
    vi.mocked(authApi.me).mockResolvedValueOnce({ data: { id: 'admin', roles: ['Admin'] } } as Awaited<ReturnType<typeof authApi.me>>);
    vi.mocked(authApi.logout).mockResolvedValueOnce({ data: {} } as Awaited<ReturnType<typeof authApi.logout>>);
    client.setQueryData(['users'], [{ id: 'private-account' }]);
    mount();
    await waitFor(() => expect(auth.isAuthenticated).toBe(true));
    await act(async () => auth.logout());
    expect(client.getQueryData(['users'])).toBeUndefined();
    expect(getAccessToken()).toBeNull();
    expect(auth.isAuthenticated).toBe(false);
});

test('logout clears the session even when the server call fails', async () => {
    vi.mocked(authApi.me).mockResolvedValueOnce({ data: { id: 'admin', roles: ['Admin'] } } as Awaited<ReturnType<typeof authApi.me>>);
    vi.mocked(authApi.logout).mockRejectedValueOnce(new Error('offline'));
    mount();
    await waitFor(() => expect(auth.isAuthenticated).toBe(true));
    await act(async () => auth.logout());
    expect(getAccessToken()).toBeNull();
    expect(auth.isAuthenticated).toBe(false);
});

test('the refresh token is never written to browser storage on login', async () => {
    vi.mocked(restoreSession).mockResolvedValueOnce(null);
    vi.mocked(authApi.login).mockResolvedValueOnce({
        // The server omits the refresh token for cookie sessions, but be explicit that
        // even a value present in the body would not be persisted.
        data: { accessToken: 'fresh', refreshToken: 'must-not-be-stored', user: { id: 'u', roles: ['User'] } },
    } as Awaited<ReturnType<typeof authApi.login>>);
    mount();
    await waitFor(() => expect(auth.loading).toBe(false));

    await act(async () => auth.login('listener', 'Correct-Horse-9'));

    expect(auth.isAuthenticated).toBe(true);
    expect(getAccessToken()).toBe('fresh');
    expect(localStorage.getItem('refreshToken')).toBeNull();
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain('must-not-be-stored');
});
