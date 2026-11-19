import { createContext, useContext, useState, useRef, useCallback, useEffect, type ReactNode } from 'react';
import { artworkUrl, reportPlay, streamUrl } from '../api/client';
import { errorMessage } from './Feedback';
import { useAuth } from '../auth/AuthContext';

/**
 * A track as the player needs it. The optional fields accept null as well as undefined
 * because that is what the API sends for an absent value.
 */
export interface Track {
    id: string;
    title: string;
    artistName?: string | null;
    albumTitle?: string | null;
    artworkId?: string | null;
    durationSeconds?: number | null;
    albumId?: string | null;
    artistId?: string | null;
}
export type RepeatMode = 'off' | 'all' | 'one';
interface PlayerContextType {
    currentTrack: Track | null; isPlaying: boolean; isBuffering: boolean; playbackError: string | null;
    currentTime: number; duration: number; volume: number; queue: Track[]; currentIndex: number; sharedRoom: string | null;
    shuffle: boolean; repeat: RepeatMode; hasNext: boolean;
    play: (track: Track, queue?: Track[]) => void;
    /** Plays a list from a random track, in random order. */
    playShuffled: (tracks: Track[]) => void;
    /** Puts a track straight after the one playing. Starts it if nothing is playing. */
    playNext: (track: Track) => void;
    /** Adds tracks to the end of the queue. Starts them if nothing is playing. */
    addToQueue: (tracks: Track[]) => void;
    playShared: (track: Track, position: number, roomName: string) => void;
    pause: () => void; resume: () => void; stop: () => void; seek: (time: number) => void;
    setVolume: (vol: number) => void; next: () => void; previous: () => void; playIndex: (index: number) => void; retry: () => void;
    toggleShuffle: () => void; cycleRepeat: () => void;
}
const PlayerContext = createContext<PlayerContextType | null>(null);
export const usePlayer = () => {
    const value = useContext(PlayerContext);
    if (!value) throw new Error('usePlayer must be inside PlayerProvider');
    return value;
};

// Remembered between visits. Storage can be unavailable (private mode), which must not
// stop the music.
const remembered = <T,>(key: string, fallback: T, valid: (value: unknown) => value is T): T => {
    try {
        const raw = localStorage.getItem(`sonafly-player-${key}`);
        const value: unknown = raw === null ? null : JSON.parse(raw);
        return valid(value) ? value : fallback;
    } catch { return fallback; }
};
const remember = (key: string, value: unknown) => {
    try { localStorage.setItem(`sonafly-player-${key}`, JSON.stringify(value)); } catch { /* not remembered */ }
};
const isVolume = (v: unknown): v is number => typeof v === 'number' && v >= 0 && v <= 1;
const isBoolean = (v: unknown): v is boolean => typeof v === 'boolean';
const isRepeat = (v: unknown): v is RepeatMode => v === 'off' || v === 'all' || v === 'one';

const shuffled = <T,>(items: T[]): T[] => {
    const copy = [...items];
    for (let i = copy.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [copy[i], copy[j]] = [copy[j], copy[i]];
    }
    return copy;
};

/** Where typing a key means something else, so the player's shortcuts stay out of the way. */
const SHORTCUT_BLOCKERS = 'input, textarea, select, button, a, [contenteditable="true"], [role="slider"], [role="button"], [role="dialog"], [role="menu"], [role="listbox"]';

export function PlayerProvider({ children }: { children: ReactNode }) {
    const { isAuthenticated } = useAuth();
    const audioRef = useRef<HTMLAudioElement | null>(null);
    const requestRef = useRef(0);
    const desired = useRef(false);
    const listRef = useRef<Track[]>([]);
    // The queue in the order it was given, kept so turning shuffle off can restore it.
    const orderedRef = useRef<Track[]>([]);
    const indexRef = useRef(0);
    const sharedRef = useRef<string | null>(null);
    const volumeRef = useRef(remembered('volume', 1, isVolume));
    const shuffleRef = useRef(remembered('shuffle', false, isBoolean));
    const repeatRef = useRef<RepeatMode>(remembered('repeat', 'off', isRepeat));
    const endedRef = useRef<() => void>(() => {});
    const retryRef = useRef<() => void>(() => {});
    const [currentTrack, setCurrentTrack] = useState<Track | null>(null);
    const [isPlaying, setIsPlaying] = useState(false);
    const [isBuffering, setIsBuffering] = useState(false);
    const [playbackError, setPlaybackError] = useState<string | null>(null);
    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [volume, setVolumeState] = useState(volumeRef.current);
    const [queue, setQueue] = useState<Track[]>([]);
    const [currentIndex, setCurrentIndex] = useState(0);
    const [sharedRoom, setSharedRoom] = useState<string | null>(null);
    const [shuffle, setShuffle] = useState(shuffleRef.current);
    const [repeat, setRepeat] = useState<RepeatMode>(repeatRef.current);

    const start = useCallback(async (track: Track, tracks: Track[], index: number, room: string | null = null, position = 0) => {
        const audio = audioRef.current;
        if (!audio) return;
        const request = ++requestRef.current;
        const requestedAt = performance.now();
        desired.current = true;
        sharedRef.current = room;
        listRef.current = tracks;
        indexRef.current = index;
        setQueue(tracks); setCurrentIndex(index); setSharedRoom(room);
        audio.pause(); audio.onloadedmetadata = null; audio.removeAttribute('src'); audio.load();
        setCurrentTrack(track); setCurrentTime(position); setDuration(track.durationSeconds ?? 0);
        setPlaybackError(null); setIsBuffering(true);
        retryRef.current = () => { void start(track, tracks, index, room, room ? position + (performance.now() - requestedAt) / 1000 : audio.currentTime || position); };
        try {
            const url = await streamUrl(track.id);
            if (request !== requestRef.current || audioRef.current !== audio) return;
            audio.onloadedmetadata = () => {
                if (request !== requestRef.current) return;
                const target = position + (room ? (performance.now() - requestedAt) / 1000 : 0);
                if (target > 0 && Number.isFinite(audio.duration)) audio.currentTime = Math.min(target, Math.max(0, audio.duration - 0.1));
                setDuration(Number.isFinite(audio.duration) ? audio.duration : track.durationSeconds ?? 0);
            };
            audio.src = url; audio.volume = volumeRef.current;
            if (desired.current) { await audio.play(); reportPlay(track.id); }
            else setIsBuffering(false);
        } catch (error) {
            if (request !== requestRef.current || audioRef.current !== audio) return;
            setIsPlaying(false); setIsBuffering(false);
            setPlaybackError(error instanceof DOMException && error.name === 'NotAllowedError' ? 'Your browser needs a tap to start audio. Select Retry to listen.' : errorMessage(error));
        }
    }, []);
    const stop = useCallback(() => {
        ++requestRef.current; desired.current = false; sharedRef.current = null;
        listRef.current = []; orderedRef.current = []; indexRef.current = 0;
        const audio = audioRef.current;
        if (audio) { audio.pause(); audio.onloadedmetadata = null; audio.removeAttribute('src'); audio.load(); }
        setCurrentTrack(null); setSharedRoom(null); setIsPlaying(false); setIsBuffering(false); setPlaybackError(null); setCurrentTime(0); setDuration(0);
        setQueue([]); setCurrentIndex(0);
    }, []);
    const playIndex = useCallback((index: number) => {
        if (sharedRef.current || !listRef.current[index]) return;
        void start(listRef.current[index], listRef.current, index);
    }, [start]);

    /** Replaces the queue without disturbing the track that is playing. */
    const setList = useCallback((tracks: Track[], index: number) => {
        listRef.current = tracks; indexRef.current = index;
        setQueue(tracks); setCurrentIndex(index);
    }, []);

    const next = useCallback(() => {
        const last = listRef.current.length - 1;
        if (indexRef.current < last) playIndex(indexRef.current + 1);
        else if (repeatRef.current === 'all') playIndex(0);
    }, [playIndex]);
    const previous = useCallback(() => {
        const audio = audioRef.current;
        // Like any player: a little way into a track, "previous" means "start it again".
        if (!sharedRef.current && audio && (audio.currentTime > 3 || indexRef.current === 0)) {
            if (audio.readyState) audio.currentTime = 0;
            return;
        }
        playIndex(indexRef.current - 1);
    }, [playIndex]);

    useEffect(() => {
        endedRef.current = () => {
            if (repeatRef.current === 'one') playIndex(indexRef.current);
            else next();
        };
    }, [playIndex, next]);
    useEffect(() => {
        // Alias the ref object, not its value. Cleanup must bump the *live* counter so
        // any start() still in flight sees a stale token and abandons itself; copying
        // requestRef.current here would increment a snapshot and defeat that.
        const pendingRequest = requestRef;
        const audio = new Audio(); audioRef.current = audio;
        audio.addEventListener('timeupdate', () => setCurrentTime(audio.currentTime));
        audio.addEventListener('playing', () => { setIsPlaying(true); setIsBuffering(false); });
        audio.addEventListener('pause', () => setIsPlaying(false));
        audio.addEventListener('waiting', () => { if (desired.current) setIsBuffering(true); });
        audio.addEventListener('canplay', () => setIsBuffering(false));
        audio.addEventListener('error', () => { if (audio.getAttribute('src')) { setPlaybackError('This track could not be played. Retry or choose another track.'); setIsBuffering(false); setIsPlaying(false); } });
        audio.addEventListener('ended', () => { setIsPlaying(false); if (!sharedRef.current) endedRef.current(); });
        return () => { ++pendingRequest.current; audioRef.current = null; audio.pause(); audio.removeAttribute('src'); audio.load(); };
    }, []);
    useEffect(() => { if (!isAuthenticated) stop(); }, [isAuthenticated, stop]);

    const play = useCallback((track: Track, tracks?: Track[]) => {
        window.dispatchEvent(new Event('sonafly-personal-playback'));
        const list = tracks?.length ? tracks : [track];
        const index = Math.max(0, list.findIndex(t => t.id === track.id));
        orderedRef.current = list;
        if (shuffleRef.current && list.length > 1) {
            // The chosen track first, then the rest in random order.
            void start(track, [list[index], ...shuffled(list.filter((_, i) => i !== index))], 0);
        } else {
            void start(track, list, index);
        }
    }, [start]);
    const playShuffled = useCallback((tracks: Track[]) => {
        if (!tracks.length) return;
        window.dispatchEvent(new Event('sonafly-personal-playback'));
        if (!shuffleRef.current) { shuffleRef.current = true; setShuffle(true); remember('shuffle', true); }
        const order = shuffled(tracks);
        orderedRef.current = tracks;
        void start(order[0], order, 0);
    }, [start]);
    const playNext = useCallback((track: Track) => {
        if (sharedRef.current) return;
        if (!listRef.current.length) { play(track); return; }
        const at = indexRef.current + 1;
        setList([...listRef.current.slice(0, at), track, ...listRef.current.slice(at)], indexRef.current);
        orderedRef.current = [...orderedRef.current, track];
    }, [play, setList]);
    const addToQueue = useCallback((tracks: Track[]) => {
        if (sharedRef.current || !tracks.length) return;
        if (!listRef.current.length) { play(tracks[0], tracks); return; }
        setList([...listRef.current, ...tracks], indexRef.current);
        orderedRef.current = [...orderedRef.current, ...tracks];
    }, [play, setList]);
    const toggleShuffle = useCallback(() => {
        const on = !shuffleRef.current;
        shuffleRef.current = on; setShuffle(on); remember('shuffle', on);
        const current = listRef.current[indexRef.current];
        if (sharedRef.current || !current) return;
        if (on) {
            // What is playing stays put; only what comes after it is reordered.
            const rest = listRef.current.filter((_, i) => i !== indexRef.current);
            setList([current, ...shuffled(rest)], 0);
        } else {
            const ordered = orderedRef.current.length ? orderedRef.current : listRef.current;
            setList(ordered, Math.max(0, ordered.indexOf(current)));
        }
    }, [setList]);
    const cycleRepeat = useCallback(() => {
        const mode: RepeatMode = repeatRef.current === 'off' ? 'all' : repeatRef.current === 'all' ? 'one' : 'off';
        repeatRef.current = mode; setRepeat(mode); remember('repeat', mode);
    }, []);
    const playShared = useCallback((track: Track, position: number, room: string) => { void start(track, [track], 0, room, position); }, [start]);
    const pause = useCallback(() => { if (sharedRef.current) return; desired.current = false; setIsBuffering(false); audioRef.current?.pause(); }, []);
    const resume = useCallback(() => {
        if (sharedRef.current) return;
        desired.current = true;
        if (audioRef.current?.getAttribute('src')) audioRef.current.play().catch(e => setPlaybackError(errorMessage(e)));
    }, []);
    const seek = useCallback((time: number) => { if (!sharedRef.current && audioRef.current?.readyState && Number.isFinite(time)) audioRef.current.currentTime = time; }, []);
    const setVolume = useCallback((value: number) => { const v = Math.max(0, Math.min(1, value)); volumeRef.current = v; setVolumeState(v); remember('volume', v); if (audioRef.current) audioRef.current.volume = v; }, []);

    // Keyboard: space plays and pauses, the arrows skip ten seconds, shift+arrows change track.
    useEffect(() => {
        const onKey = (event: KeyboardEvent) => {
            if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
            const audio = audioRef.current;
            if (!audio || !listRef.current.length || sharedRef.current) return;
            if (event.target instanceof Element && event.target.closest(SHORTCUT_BLOCKERS)) return;
            if (event.key === ' ') { if (desired.current) pause(); else resume(); }
            else if (event.key === 'ArrowRight' && event.shiftKey) next();
            else if (event.key === 'ArrowLeft' && event.shiftKey) previous();
            else if (event.key === 'ArrowRight') seek(Math.min(audio.currentTime + 10, Number.isFinite(audio.duration) ? audio.duration - 0.5 : audio.currentTime + 10));
            else if (event.key === 'ArrowLeft') seek(Math.max(0, audio.currentTime - 10));
            else return;
            event.preventDefault();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [pause, resume, next, previous, seek]);

    // Media keys, headset buttons and the operating system's "now playing" panel.
    useEffect(() => {
        if (!('mediaSession' in navigator)) return;
        const session = navigator.mediaSession;
        if (!currentTrack) { session.metadata = null; return; }
        const cover = artworkUrl(currentTrack.artworkId, 640);
        session.metadata = new MediaMetadata({
            title: currentTrack.title,
            artist: currentTrack.artistName ?? '',
            album: currentTrack.albumTitle ?? '',
            artwork: cover ? [{ src: cover, sizes: '640x640' }] : [],
        });
        const personal = !sharedRoom;
        session.setActionHandler('play', personal ? resume : null);
        session.setActionHandler('pause', personal ? pause : null);
        session.setActionHandler('previoustrack', personal ? previous : null);
        session.setActionHandler('nexttrack', personal ? next : null);
        session.setActionHandler('seekto', personal ? details => { if (details.seekTime != null) seek(details.seekTime); } : null);
    }, [currentTrack, sharedRoom, resume, pause, previous, next, seek]);
    useEffect(() => {
        if ('mediaSession' in navigator) navigator.mediaSession.playbackState = !currentTrack ? 'none' : isPlaying ? 'playing' : 'paused';
    }, [currentTrack, isPlaying]);

    const hasNext = currentIndex < queue.length - 1 || (repeat === 'all' && queue.length > 0);
    return <PlayerContext.Provider value={{ currentTrack, isPlaying, isBuffering, playbackError, currentTime, duration, volume, queue, currentIndex, sharedRoom,
        shuffle, repeat, hasNext,
        play, playShuffled, playNext, addToQueue, playShared, pause, resume, stop, seek, setVolume, next, previous, playIndex,
        retry: () => retryRef.current(), toggleShuffle, cycleRepeat }}>{children}</PlayerContext.Provider>;
}
