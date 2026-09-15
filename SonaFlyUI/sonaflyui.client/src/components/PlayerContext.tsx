import { createContext, useContext, useState, useRef, useCallback, useEffect, type ReactNode } from 'react';
import { streamUrl } from '../api/client';
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
}
interface PlayerContextType {
    currentTrack: Track | null; isPlaying: boolean; isBuffering: boolean; playbackError: string | null;
    currentTime: number; duration: number; volume: number; queue: Track[]; currentIndex: number; sharedRoom: string | null;
    play: (track: Track, queue?: Track[]) => void;
    playShared: (track: Track, position: number, roomName: string) => void;
    pause: () => void; resume: () => void; stop: () => void; seek: (time: number) => void;
    setVolume: (vol: number) => void; next: () => void; previous: () => void; playIndex: (index: number) => void; retry: () => void;
}
const PlayerContext = createContext<PlayerContextType | null>(null);
export const usePlayer = () => {
    const value = useContext(PlayerContext);
    if (!value) throw new Error('usePlayer must be inside PlayerProvider');
    return value;
};
export function PlayerProvider({ children }: { children: ReactNode }) {
    const { isAuthenticated } = useAuth();
    const audioRef = useRef<HTMLAudioElement | null>(null);
    const requestRef = useRef(0);
    const desired = useRef(false);
    const listRef = useRef<Track[]>([]);
    const indexRef = useRef(0);
    const sharedRef = useRef<string | null>(null);
    const volumeRef = useRef(1);
    const nextRef = useRef<() => void>(() => {});
    const retryRef = useRef<() => void>(() => {});
    const [currentTrack, setCurrentTrack] = useState<Track | null>(null);
    const [isPlaying, setIsPlaying] = useState(false);
    const [isBuffering, setIsBuffering] = useState(false);
    const [playbackError, setPlaybackError] = useState<string | null>(null);
    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [volume, setVolumeState] = useState(1);
    const [queue, setQueue] = useState<Track[]>([]);
    const [currentIndex, setCurrentIndex] = useState(0);
    const [sharedRoom, setSharedRoom] = useState<string | null>(null);

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
            if (desired.current) await audio.play();
            else setIsBuffering(false);
        } catch (error) {
            if (request !== requestRef.current || audioRef.current !== audio) return;
            setIsPlaying(false); setIsBuffering(false);
            setPlaybackError(error instanceof DOMException && error.name === 'NotAllowedError' ? 'Your browser needs a tap to start audio. Select Retry to listen.' : errorMessage(error));
        }
    }, []);
    const stop = useCallback(() => {
        ++requestRef.current; desired.current = false; sharedRef.current = null;
        const audio = audioRef.current;
        if (audio) { audio.pause(); audio.onloadedmetadata = null; audio.removeAttribute('src'); audio.load(); }
        setCurrentTrack(null); setSharedRoom(null); setIsPlaying(false); setIsBuffering(false); setPlaybackError(null); setCurrentTime(0); setDuration(0);
    }, []);
    const playIndex = useCallback((index: number) => {
        if (sharedRef.current || !listRef.current[index]) return;
        void start(listRef.current[index], listRef.current, index);
    }, [start]);
    useEffect(() => { nextRef.current = () => playIndex(indexRef.current + 1); }, [playIndex]);
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
        audio.addEventListener('ended', () => { setIsPlaying(false); if (!sharedRef.current) nextRef.current(); });
        return () => { ++pendingRequest.current; audioRef.current = null; audio.pause(); audio.removeAttribute('src'); audio.load(); };
    }, []);
    useEffect(() => { if (!isAuthenticated) stop(); }, [isAuthenticated, stop]);
    const play = useCallback((track: Track, tracks?: Track[]) => {
        window.dispatchEvent(new Event('sonafly-personal-playback'));
        const list = tracks?.length ? tracks : [track];
        void start(track, list, Math.max(0, list.findIndex(t => t.id === track.id)));
    }, [start]);
    const playShared = useCallback((track: Track, position: number, room: string) => { void start(track, [track], 0, room, position); }, [start]);
    const pause = () => { if (sharedRef.current) return; desired.current = false; setIsBuffering(false); audioRef.current?.pause(); };
    const resume = () => {
        if (sharedRef.current) return;
        desired.current = true;
        if (audioRef.current?.getAttribute('src')) audioRef.current.play().catch(e => setPlaybackError(errorMessage(e)));
    };
    const seek = (time: number) => { if (!sharedRef.current && audioRef.current?.readyState && Number.isFinite(time)) audioRef.current.currentTime = time; };
    const setVolume = (value: number) => { const v = Math.max(0, Math.min(1, value)); volumeRef.current = v; setVolumeState(v); if (audioRef.current) audioRef.current.volume = v; };
    return <PlayerContext.Provider value={{ currentTrack, isPlaying, isBuffering, playbackError, currentTime, duration, volume, queue, currentIndex, sharedRoom,
        play, playShared, pause, resume, stop, seek, setVolume, next: () => playIndex(indexRef.current + 1), previous: () => playIndex(indexRef.current - 1), playIndex, retry: () => retryRef.current() }}>{children}</PlayerContext.Provider>;
}
