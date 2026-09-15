import { test, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import axios, { AxiosError } from 'axios';
import api, { authApi, restoreSession } from './client.ts';
import { getAccessToken, resetSession, setAccessToken } from './session.ts';

const originalPost = axios.post;
let values;

beforeEach(() => {
    values = new Map();
    globalThis.localStorage = {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value),
        removeItem: key => values.delete(key),
    };
    globalThis.window = { location: { href: '' } };
    resetSession();
    setAccessToken('old');
});

afterEach(() => { axios.post = originalPost; resetSession(); });

const rejection = (status, config = {}) => new AxiosError(
    `Status ${status}`, 'ERR_BAD_RESPONSE', config, null,
    { status, data: {}, headers: {}, config });

const unauthorized = config => { throw rejection(401, config); };
const ok = config => ({ status: 200, data: {}, headers: {}, config });

test('concurrent 401s share one refresh, including auth/me', async () => {
    let refreshes = 0;
    axios.post = async () => {
        refreshes++;
        await new Promise(resolve => setTimeout(resolve, 20));
        return { data: { accessToken: 'new' } };
    };
    api.defaults.adapter = async config => config.headers.Authorization === 'Bearer new'
        ? ok(config) : unauthorized(config);
    await Promise.all([api.get('/tracks'), api.get('/albums'), api.get('/auth/me')]);
    assert.equal(refreshes, 1);
    assert.equal(getAccessToken(), 'new');
    assert.equal(window.location.href, '');
});

test('a late 401 retries the already rotated access token', async () => {
    let refreshes = 0;
    axios.post = async () => {
        refreshes++;
        return { data: { accessToken: 'new' } };
    };
    api.defaults.adapter = async config => {
        if (config.headers.Authorization === 'Bearer new') return ok(config);
        if (config.url === '/slow') await new Promise(resolve => setTimeout(resolve, 30));
        return unauthorized(config);
    };
    await Promise.all([api.get('/fast'), api.get('/slow')]);
    assert.equal(refreshes, 1);
    assert.equal(window.location.href, '');
});

test('invalid login credentials do not refresh or redirect', async () => {
    axios.post = async () => assert.fail('refresh must not run');
    api.defaults.adapter = async config => unauthorized(config);
    await assert.rejects(api.post('/auth/login', {}));
    assert.equal(window.location.href, '');
});

test('a refresh finishing after logout cannot restore credentials', async () => {
    axios.post = async () => {
        // Logout happens while the refresh is in flight.
        resetSession();
        return { data: { accessToken: 'new' } };
    };
    api.defaults.adapter = async config => unauthorized(config);
    await assert.rejects(api.get('/tracks'));
    assert.equal(getAccessToken(), null);
});

test('the refresh token is never sent in the request body or stored', async () => {
    let body;
    axios.post = async (_url, data) => { body = data; return { data: { accessToken: 'new' } }; };
    api.defaults.adapter = async config => config.headers.Authorization === 'Bearer new'
        ? ok(config) : unauthorized(config);
    await api.get('/tracks');
    // The cookie carries it; nothing script-readable does.
    assert.deepEqual(body, {});
    assert.equal(values.size, 0);
});

test('requests carry the client header that guards the cookie', async () => {
    let seen;
    api.defaults.adapter = async config => { seen = config.headers; return ok(config); };
    await api.get('/tracks');
    assert.equal(seen['X-SonaFly-Client'], 'web');
});

test('restoreSession trades a legacy localStorage token for a cookie and erases it', async () => {
    values.set('accessToken', 'legacy-access');
    values.set('refreshToken', 'legacy-refresh');
    resetSession();

    let sent;
    axios.post = async (_url, data) => { sent = data; return { data: { accessToken: 'cookie-backed' } }; };

    assert.equal(await restoreSession(), 'cookie-backed');
    assert.equal(sent.refreshToken, 'legacy-refresh');
    assert.equal(sent.useCookie, true);
    // Both plaintext copies are gone, which is the point of the migration.
    assert.equal(values.get('refreshToken'), undefined);
    assert.equal(values.get('accessToken'), undefined);
    assert.equal(getAccessToken(), 'cookie-backed');
});

test('restoreSession erases a legacy token even when the exchange fails', async () => {
    values.set('refreshToken', 'legacy-refresh');
    resetSession();
    axios.post = async () => { throw rejection(401); };

    assert.equal(await restoreSession(), null);
    assert.equal(values.get('refreshToken'), undefined);
    assert.equal(getAccessToken(), null);
});

// ── One rotation at a time ──
//
// The cookie rotates on every use and the server revokes the whole chain when a token is
// presented twice, so overlapping rotations do not just waste a request: they end the
// session. StrictMode mounts twice and a second tab shares the same cookie, so this is the
// ordinary case, not an exotic one.

test('concurrent restorations share a single rotation', async () => {
    resetSession();
    let posts = 0;
    axios.post = async () => {
        posts++;
        await new Promise(resolve => setTimeout(resolve, 20));
        return { data: { accessToken: 'restored' } };
    };

    // What React StrictMode does to the mount effect in development.
    const [first, second] = await Promise.all([restoreSession(), restoreSession()]);

    assert.equal(posts, 1);
    assert.equal(first, 'restored');
    assert.equal(second, 'restored');
});

test('a restoration and a 401 retry share a single rotation', async () => {
    resetSession();
    setAccessToken('old');
    let posts = 0;
    axios.post = async () => {
        posts++;
        await new Promise(resolve => setTimeout(resolve, 20));
        return { data: { accessToken: 'new' } };
    };
    api.defaults.adapter = async config => config.headers.Authorization === 'Bearer new'
        ? ok(config) : unauthorized(config);

    await Promise.all([restoreSession(), api.get('/tracks')]);

    assert.equal(posts, 1);
});

test('rotations queue behind each other rather than overlapping', async () => {
    resetSession();
    let inFlight = 0;
    let overlapped = false;
    axios.post = async () => {
        inFlight++;
        if (inFlight > 1) overlapped = true;
        await new Promise(resolve => setTimeout(resolve, 10));
        inFlight--;
        return { data: { accessToken: 'rotated' } };
    };

    // Deliberately sequential calls that each start a new rotation, interleaved in time.
    const first = restoreSession();
    await new Promise(resolve => setTimeout(resolve, 1));
    const second = restoreSession();
    await Promise.all([first, second]);

    assert.equal(overlapped, false);
});

test('a rotation is held across tabs when the Web Locks API is available', async () => {
    resetSession();
    const held = [];
    // globalThis.navigator is getter-only under Node, so install the stub as a property.
    const original = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
    Object.defineProperty(globalThis, 'navigator', {
        configurable: true,
        value: {
            locks: {
                request: async (name, callback) => {
                    held.push(name);
                    return callback({ name, mode: 'exclusive' });
                },
            },
        },
    });
    axios.post = async () => ({ data: { accessToken: 'restored' } });

    try {
        assert.equal(await restoreSession(), 'restored');
        assert.deepEqual(held, ['sonafly-session']);
    } finally {
        if (original) Object.defineProperty(globalThis, 'navigator', original);
        else delete globalThis.navigator;
    }
});

test('logout waits for an outstanding rotation instead of racing it', async () => {
    resetSession();
    setAccessToken('old');
    const order = [];
    axios.post = async () => {
        order.push('rotation-start');
        await new Promise(resolve => setTimeout(resolve, 20));
        order.push('rotation-end');
        return { data: { accessToken: 'new' } };
    };
    api.defaults.adapter = async config => {
        if (config.url === '/auth/logout') { order.push('logout'); return ok(config); }
        return config.headers.Authorization === 'Bearer new' ? ok(config) : unauthorized(config);
    };

    const rotating = api.get('/tracks');
    await new Promise(resolve => setTimeout(resolve, 1));
    await Promise.all([rotating, authApi.logout()]);

    // The logout must not land while the cookie is mid-rotation, or the browser keeps a
    // credential the user believes they have given up.
    assert.deepEqual(order, ['rotation-start', 'rotation-end', 'logout']);
});

// ── Transient failures are not a signed-out user ──

test('restoreSession reports no session only when the server refuses the credential', async () => {
    for (const status of [400, 401, 403]) {
        resetSession();
        axios.post = async config => { throw rejection(status, config); };
        assert.equal(await restoreSession(), null, `status ${status}`);
    }
});

test('restoreSession propagates an outage rather than reporting a signed-out user', async () => {
    for (const status of [429, 500, 503]) {
        resetSession();
        axios.post = async config => { throw rejection(status, config); };
        await assert.rejects(restoreSession(), `status ${status} must be retryable`);
    }
});

test('restoreSession propagates a dropped connection', async () => {
    resetSession();
    // No response at all: the session may well be perfectly good.
    axios.post = async () => { throw new AxiosError('Network Error', 'ERR_NETWORK'); };

    await assert.rejects(restoreSession(), { message: 'Network Error' });
    assert.equal(getAccessToken(), null);
});

test('a failed rotation does not wedge the next attempt', async () => {
    resetSession();
    axios.post = async () => { throw new AxiosError('Network Error', 'ERR_NETWORK'); };
    await assert.rejects(restoreSession());

    axios.post = async () => ({ data: { accessToken: 'second-time-lucky' } });
    assert.equal(await restoreSession(), 'second-time-lucky');
});

test('a rotation that finishes after logout is discarded, not thrown', async () => {
    resetSession();
    axios.post = async () => {
        resetSession();
        return { data: { accessToken: 'stale' } };
    };

    assert.equal(await restoreSession(), null);
    assert.equal(getAccessToken(), null);
});

test('restoreSession with no legacy token refreshes from the cookie alone', async () => {
    resetSession();
    let sent;
    axios.post = async (_url, data) => { sent = data; return { data: { accessToken: 'cookie-backed' } }; };

    assert.equal(await restoreSession(), 'cookie-backed');
    assert.deepEqual(sent, {});
});
