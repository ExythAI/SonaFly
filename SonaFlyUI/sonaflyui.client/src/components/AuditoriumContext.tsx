import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
// Type-only: erased at build time, so the SignalR client stays out of the initial
// bundle. The implementation is imported on demand in join(), because most sessions
// never open an auditorium and it is a sizeable dependency to pay for up front.
import type { HubConnection } from '@microsoft/signalr';
import api from '../api/client';
import { getAccessToken } from '../api/session';
import { useAuth } from '../auth/AuthContext';
import { usePlayer } from './PlayerContext';
import { errorMessage, notify } from './Feedback';

export type Room = { id: string; name: string; activeUserCount: number; nowPlaying?: string };
export type RoomUser = { userId: string; displayName: string };
export type RoomQueueItem = { id: string; trackId: string; title: string; artistName?: string; queuedByUserId: string; queuedByUserName: string };
export type RoomState = {
    currentTrackId?: string; currentTrackTitle?: string; currentArtistName?: string; currentArtworkId?: string;
    currentTrackDuration?: number; currentPositionSeconds: number; isPaused: boolean;
    startedByUserId?: string; activeUsers: RoomUser[]; queue: RoomQueueItem[]; serverUtcNow: string;
};
type AuditoriumContextType = {
    room: Room | null; snapshot: RoomState | null; connectionStatus: string; busy: boolean; error: string | null;
    /** True when the room's current track is restricted for this account. Not an error. */
    trackUnavailable: boolean;
    join: (room: Room) => Promise<void>; leave: () => Promise<void>;
    command: (name: 'QueueTrack' | 'RemoveFromQueue' | 'SkipTrack' | 'StopTrack', id?: string) => Promise<boolean>;
};
const AuditoriumContext = createContext<AuditoriumContextType | null>(null);
export const useAuditorium = () => {
    const context = useContext(AuditoriumContext);
    if (!context) throw new Error('Missing AuditoriumProvider');
    return context;
};

export function AuditoriumProvider({ children }: { children: ReactNode }) {
    const { isAuthenticated } = useAuth();
    const { playShared, stop } = usePlayer();
    const hubRef = useRef<HubConnection | null>(null);
    const revision = useRef(0);
    /**
     * Bumped by every snapshot and every connection event, so a playability check still in
     * flight can tell that the room has moved on since it was asked.
     *
     * The connection revision is not enough on its own: it does not change between one track
     * and the next, nor when a reconnect stops the audio, which is exactly when a slow
     * answer about the previous track would arrive and start it playing again.
     */
    const playbackGeneration = useRef(0);
    const operation = useRef(false);
    const [room, setRoom] = useState<Room | null>(null);
    const [snapshot, setSnapshot] = useState<RoomState | null>(null);
    const [connectionStatus, setConnectionStatus] = useState('Disconnected');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [trackUnavailable, setTrackUnavailable] = useState(false);

    const disconnect = useCallback(async (stopAudio = true) => {
        revision.current++;
        playbackGeneration.current++;
        const connection = hubRef.current;
        hubRef.current = null;
        setRoom(null); setSnapshot(null); setConnectionStatus('Disconnected'); setBusy(false); setError(null);
        setTrackUnavailable(false);
        if (stopAudio) stop();
        if (connection) { try { await connection.stop(); } catch { /* already disconnected */ } }
    }, [stop]);
    useEffect(() => {
        const personalPlayback = () => { void disconnect(false); };
        window.addEventListener('sonafly-personal-playback', personalPlayback);
        return () => { window.removeEventListener('sonafly-personal-playback', personalPlayback); void hubRef.current?.stop(); };
    }, [disconnect]);
    useEffect(() => { if (!isAuthenticated) void disconnect(); }, [isAuthenticated, disconnect]);

    const join = useCallback(async (selected: Room) => {
        const leaving = disconnect();
        const current = revision.current;
        setBusy(true); setConnectionStatus('Connecting'); setError(null);
        await leaving;
        if (current !== revision.current) return;
        const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
        if (current !== revision.current) return;

        const hub = new HubConnectionBuilder().withUrl('/hubs/auditorium', {
            accessTokenFactory: async () => {
                // Uses the same single refresh operation as all other API requests.
                await api.get('/auth/me');
                return getAccessToken() ?? '';
            },
        }).withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(LogLevel.Warning).build();
        hubRef.current = hub;
        const apply = async (state: RoomState) => {
            if (current !== revision.current) return;
            // This snapshot supersedes whatever was being applied before it.
            const generation = ++playbackGeneration.current;
            const receivedAt = Date.now();
            setSnapshot(state);
            if (!state.currentTrackId || state.isPaused) { setTrackUnavailable(false); stop(); return; }

            // The room admits a track based on the queuer's restrictions; this listener's
            // own may differ. Ask before playing, so a restricted listener gets a stated
            // reason rather than a generic stream failure.
            let playable = true;
            try {
                const answer = await hub.invoke<{ trackId: string | null; playable: boolean }>('CanPlayCurrentTrack');
                // The server answers about whatever the room is playing when the question
                // reaches it, which need not be the track in this snapshot any more.
                if (answer.trackId?.toLowerCase() !== state.currentTrackId.toLowerCase()) return;
                playable = answer.playable;
            } catch {
                // If the check itself fails, attempt playback: a working stream should not
                // be suppressed because one extra call did not come back.
                playable = true;
            }
            // A newer snapshot, a reconnect, a close or a leave while the answer was in
            // flight all make this answer worthless — and acting on it would resume audio
            // that something else has deliberately stopped.
            if (current !== revision.current || generation !== playbackGeneration.current) return;

            setTrackUnavailable(!playable);
            if (!playable) { stop(); return; }

            // Start where the room is now, not where it was when the snapshot was sent:
            // the check above took time, and everyone else kept listening through it.
            const elapsedSinceSnapshot = Math.max(0, (Date.now() - receivedAt) / 1000);
            playShared({ id: state.currentTrackId,
                title: state.currentTrackTitle || 'Untitled', artistName: state.currentArtistName,
                artworkId: state.currentArtworkId, durationSeconds: state.currentTrackDuration },
                state.currentPositionSeconds + elapsedSinceSnapshot, selected.name);
        };
        hub.on('OnRoomClosed', () => { if (current === revision.current) { void disconnect(); notify('This auditorium was closed by an administrator.', 'info'); } });
        hub.on('OnTrackStarted', (state: RoomState) => { void apply(state); });
        hub.on('OnTrackEnded', (state: RoomState) => { void apply(state); });
        hub.on('OnQueueUpdated', (queue: RoomQueueItem[]) => { if (current === revision.current) setSnapshot(s => s ? { ...s, queue } : s); });
        const presence = (_name: string, activeUsers: RoomUser[]) => { if (current === revision.current) setSnapshot(s => s ? { ...s, activeUsers } : s); };
        hub.on('OnUserJoined', presence); hub.on('OnUserLeft', presence);
        hub.onreconnecting(() => { if (current === revision.current) { playbackGeneration.current++; setConnectionStatus('Reconnecting'); stop(); } });
        hub.onreconnected(async () => {
            if (current !== revision.current) return;
            try {
                await apply(await hub.invoke<RoomState>('JoinAuditorium', selected.id));
                if (current === revision.current) { setConnectionStatus('Connected'); setError(null); }
            } catch (e) { if (current === revision.current) { playbackGeneration.current++; setError(errorMessage(e)); setConnectionStatus('Disconnected'); stop(); } }
        });
        hub.onclose(() => { if (current === revision.current) { playbackGeneration.current++; setConnectionStatus('Disconnected'); setError('The room disconnected. Select Rejoin to reconnect.'); stop(); } });
        try {
            await hub.start();
            if (current !== revision.current) { await hub.stop(); return; }
            const state = await hub.invoke<RoomState>('JoinAuditorium', selected.id);
            if (current !== revision.current) { await hub.stop(); return; }
            setRoom(selected); setConnectionStatus('Connected'); await apply(state);
        } catch (e) {
            if (current === revision.current) { setError(errorMessage(e)); setConnectionStatus('Disconnected'); notify(errorMessage(e), 'error'); }
            await hub.stop();
        } finally { if (current === revision.current) setBusy(false); }
    }, [disconnect, playShared, stop]);
    const command = async (name: 'QueueTrack' | 'RemoveFromQueue' | 'SkipTrack' | 'StopTrack', id?: string) => {
        // 'Connected' rather than the HubConnectionState enum, which would pull the
        // module back into the initial bundle just for a constant.
        if (operation.current || hubRef.current?.state !== 'Connected') return false;
        const current = revision.current;
        operation.current = true; setBusy(true);
        try {
            if (id) await hubRef.current.invoke(name, id); else await hubRef.current.invoke(name);
            if (current === revision.current) { setError(null); if (name === 'QueueTrack') notify('Track added to the room queue.'); }
            return true;
        } catch (e) { if (current === revision.current) { setError(errorMessage(e)); notify(errorMessage(e), 'error'); } return false; }
        finally { operation.current = false; if (current === revision.current) setBusy(false); }
    };
    return <AuditoriumContext.Provider value={{ room, snapshot, connectionStatus, busy, error, trackUnavailable, join, leave: () => disconnect(), command }}>{children}</AuditoriumContext.Provider>;
}
