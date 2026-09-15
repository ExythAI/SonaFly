import { act, cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { AuditoriumProvider, useAuditorium, type Room, type RoomState } from './AuditoriumContext';

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true }) }));
vi.mock('../api/client', () => ({ default: { get: vi.fn().mockResolvedValue({ data: {} }) } }));
vi.mock('../api/session', () => ({ getAccessToken: () => 'token' }));
vi.mock('./Feedback', () => ({ errorMessage: (e: unknown) => String(e), notify: vi.fn() }));

const playShared = vi.fn();
const stop = vi.fn();
vi.mock('./PlayerContext', () => ({ usePlayer: () => ({ playShared, stop }) }));

/**
 * Stands in for the SignalR connection. Handlers registered with `on` are captured so a
 * test can deliver a snapshot exactly when it wants to, and `invoke` is scripted per call
 * so a slow answer can be made to arrive after a later event.
 */
class FakeHub {
    handlers = new Map<string, (...args: never[]) => void>();
    reconnecting: (() => void) | null = null;
    closed: (() => void) | null = null;
    state = 'Connected';
    invoke = vi.fn();
    on(name: string, handler: (...args: never[]) => void) { this.handlers.set(name, handler); }
    onreconnecting(handler: () => void) { this.reconnecting = handler; }
    onreconnected() { /* not exercised here */ }
    onclose(handler: () => void) { this.closed = handler; }
    async start() { /* connected immediately */ }
    async stop() { this.state = 'Disconnected'; }
    emit(name: string, payload: unknown) { this.handlers.get(name)?.(payload as never); }
}

let hub: FakeHub;
vi.mock('@microsoft/signalr', () => ({
    LogLevel: { Warning: 3 },
    HubConnectionBuilder: class {
        withUrl() { return this; }
        withAutomaticReconnect() { return this; }
        configureLogging() { return this; }
        build() { return hub; }
    },
}));

const room: Room = { id: 'room-1', name: 'Listening Room', activeUserCount: 1 };

const snapshotFor = (trackId: string, positionSeconds = 10): RoomState => ({
    currentTrackId: trackId,
    currentTrackTitle: `Track ${trackId}`,
    currentPositionSeconds: positionSeconds,
    isPaused: false,
    activeUsers: [],
    queue: [],
    serverUtcNow: new Date().toISOString(),
});

const idle: RoomState = {
    currentPositionSeconds: 0, isPaused: false, activeUsers: [], queue: [], serverUtcNow: new Date().toISOString(),
};

const playable = (trackId: string | null) => ({ trackId, playable: true });

let auditorium: ReturnType<typeof useAuditorium>;
function Probe() { auditorium = useAuditorium(); return null; }

/** Joins the room, answering the initial JoinAuditorium with an idle snapshot. */
async function joinIdleRoom() {
    hub.invoke.mockImplementation(async (name: string) =>
        name === 'JoinAuditorium' ? idle : playable(null));

    await act(async () => { await auditorium.join(room); });

    hub.invoke.mockReset();
    playShared.mockClear();
    stop.mockClear();
}

beforeEach(async () => {
    hub = new FakeHub();
    playShared.mockReset();
    stop.mockReset();
    render(<AuditoriumProvider><Probe /></AuditoriumProvider>);
    await joinIdleRoom();
});

afterEach(() => { cleanup(); });

// ── A late answer must not start audio the room has moved past ──

test('a delayed answer about track A cannot start A after B has begun', async () => {
    let answerForA!: (result: { trackId: string; playable: boolean }) => void;
    hub.invoke
        .mockImplementationOnce(() => new Promise(resolve => { answerForA = resolve as never; }))
        .mockImplementation(async () => playable('b'));

    act(() => hub.emit('OnTrackStarted', snapshotFor('a')));
    await act(async () => { hub.emit('OnTrackStarted', snapshotFor('b')); });
    await waitFor(() => expect(playShared).toHaveBeenCalled());

    // A's check comes back only now, saying A is fine to play. It is not: B is playing.
    await act(async () => { answerForA(playable('a') as never); });

    expect(playShared).toHaveBeenCalledTimes(1);
    expect(playShared.mock.calls[0][0].id).toBe('b');
});

test('a delayed answer cannot start a track after the room has ended it', async () => {
    let answerForA!: (result: { trackId: string; playable: boolean }) => void;
    hub.invoke.mockImplementationOnce(() => new Promise(resolve => { answerForA = resolve as never; }));

    act(() => hub.emit('OnTrackStarted', snapshotFor('a')));
    await act(async () => { hub.emit('OnTrackEnded', idle); });

    await act(async () => { answerForA(playable('a') as never); });

    expect(playShared).not.toHaveBeenCalled();
});

test('a delayed answer cannot restart audio that a reconnect stopped', async () => {
    let answerForA!: (result: { trackId: string; playable: boolean }) => void;
    hub.invoke.mockImplementationOnce(() => new Promise(resolve => { answerForA = resolve as never; }));

    act(() => hub.emit('OnTrackStarted', snapshotFor('a')));
    act(() => { hub.reconnecting?.(); });
    expect(stop).toHaveBeenCalled();

    await act(async () => { answerForA(playable('a') as never); });

    // The connection revision does not change on a reconnect, which is why the older code
    // resumed here.
    expect(playShared).not.toHaveBeenCalled();
});

test('an answer about a different track is discarded rather than believed', async () => {
    // The server answers about whatever the room is playing when the call lands, so a
    // yes/no alone can be an answer to a question nobody asked.
    hub.invoke.mockResolvedValueOnce({ trackId: 'something-else', playable: false });

    await act(async () => { hub.emit('OnTrackStarted', snapshotFor('a')); });

    expect(playShared).not.toHaveBeenCalled();
    expect(auditorium.trackUnavailable).toBe(false);
});

test('a leave while the check is in flight does not start playback afterwards', async () => {
    let answerForA!: (result: { trackId: string; playable: boolean }) => void;
    hub.invoke.mockImplementationOnce(() => new Promise(resolve => { answerForA = resolve as never; }));

    act(() => hub.emit('OnTrackStarted', snapshotFor('a')));
    await act(async () => { await auditorium.leave(); });

    await act(async () => { answerForA(playable('a') as never); });

    expect(playShared).not.toHaveBeenCalled();
});

// ── A valid answer still lands in the right place ──

test('a valid answer starts playback at the shared position', async () => {
    hub.invoke.mockResolvedValueOnce(playable('a'));

    await act(async () => { hub.emit('OnTrackStarted', snapshotFor('a', 30)); });

    expect(playShared).toHaveBeenCalledTimes(1);
    expect(playShared.mock.calls[0][0].id).toBe('a');
    expect(playShared.mock.calls[0][1]).toBeGreaterThanOrEqual(30);
});

test('time spent waiting for the check is added to the starting position', async () => {
    let answerForA!: (result: { trackId: string; playable: boolean }) => void;
    hub.invoke.mockImplementationOnce(() => new Promise(resolve => { answerForA = resolve as never; }));

    act(() => hub.emit('OnTrackStarted', snapshotFor('a', 30)));
    // The rest of the room keeps listening while this listener waits for an answer, so
    // starting at the snapshot's position would put them behind by however long it took.
    await new Promise(resolve => setTimeout(resolve, 120));
    await act(async () => { answerForA(playable('a') as never); });

    await waitFor(() => expect(playShared).toHaveBeenCalled());
    expect(playShared.mock.calls[0][1]).toBeGreaterThan(30);
});

test('a restricted track is reported rather than played', async () => {
    hub.invoke.mockResolvedValueOnce({ trackId: 'a', playable: false });

    await act(async () => { hub.emit('OnTrackStarted', snapshotFor('a')); });

    expect(auditorium.trackUnavailable).toBe(true);
    expect(playShared).not.toHaveBeenCalled();
    expect(stop).toHaveBeenCalled();
});

test('a failed check still attempts playback', async () => {
    // One extra call not coming back must not silence a stream that would have worked.
    hub.invoke.mockRejectedValueOnce(new Error('hub unavailable'));

    await act(async () => { hub.emit('OnTrackStarted', snapshotFor('a')); });

    expect(playShared).toHaveBeenCalledTimes(1);
});
