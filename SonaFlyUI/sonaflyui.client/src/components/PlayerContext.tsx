import React, { createContext, useContext, useState, useRef, useCallback, useEffect } from 'react';
import { streamUrl } from '../api/client';

interface Track {
    id: string;
    title: string;
    artistName?: string;
    albumTitle?: string;
    artworkId?: string;
    durationSeconds?: number;
}

interface PlayerState {
    currentTrack: Track | null;
    isPlaying: boolean;
    currentTime: number;
    duration: number;
    volume: number;
}

interface PlayerContextType extends PlayerState {
    play: (track: Track, queue?: Track[]) => void;
    pause: () => void;
    resume: () => void;
    stop: () => void;
    seek: (time: number) => void;
    setVolume: (vol: number) => void;
    next: () => void;
    previous: () => void;
    queue: Track[];
}

const PlayerContext = createContext<PlayerContextType | null>(null);

export const usePlayer = () => {
    const ctx = useContext(PlayerContext);
    if (!ctx) throw new Error('usePlayer must be inside PlayerProvider');
    return ctx;
};

export const PlayerProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const audioRef = useRef<HTMLAudioElement | null>(null);
    const [currentTrack, setCurrentTrack] = useState<Track | null>(null);
    const [isPlaying, setIsPlaying] = useState(false);
    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [volume, setVolumeState] = useState(1);
    const [queue, setQueue] = useState<Track[]>([]);

    // Create audio element once
    useEffect(() => {
        const audio = new Audio();
        audio.addEventListener('timeupdate', () => setCurrentTime(audio.currentTime));
        audio.addEventListener('loadedmetadata', () => setDuration(audio.duration));
        audio.addEventListener('ended', () => {
            // Auto-advance to next track
            setIsPlaying(false);
            setQueue(prev => {
                const idx = prev.findIndex(t => t.id === audioRef.current?.dataset.trackId);
                if (idx >= 0 && idx < prev.length - 1) {
                    const nextTrack = prev[idx + 1];
                    setTimeout(() => playTrack(nextTrack, prev), 0);
                }
                return prev;
            });
        });
        audio.addEventListener('play', () => setIsPlaying(true));
        audio.addEventListener('pause', () => setIsPlaying(false));
        audioRef.current = audio;

        return () => {
            audio.pause();
            audio.src = '';
        };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const playTrack = useCallback((track: Track, trackQueue?: Track[]) => {
        const audio = audioRef.current;
        if (!audio) return;

        audio.pause();
        audio.src = streamUrl(track.id);
        audio.dataset.trackId = track.id;
        audio.volume = volume;
        audio.play();
        setCurrentTrack(track);
        setCurrentTime(0);
        if (trackQueue) setQueue(trackQueue);
    }, [volume]);

    const pause = useCallback(() => { audioRef.current?.pause(); }, []);
    const resume = useCallback(() => { audioRef.current?.play(); }, []);
    const stop = useCallback(() => {
        const audio = audioRef.current;
        if (!audio) return;
        audio.pause();
        audio.src = '';
        setCurrentTrack(null);
        setIsPlaying(false);
        setCurrentTime(0);
        setDuration(0);
    }, []);

    const seek = useCallback((time: number) => {
        if (audioRef.current) audioRef.current.currentTime = time;
    }, []);

    const setVolume = useCallback((vol: number) => {
        setVolumeState(vol);
        if (audioRef.current) audioRef.current.volume = vol;
    }, []);

    const next = useCallback(() => {
        if (!currentTrack || queue.length === 0) return;
        const idx = queue.findIndex(t => t.id === currentTrack.id);
        if (idx >= 0 && idx < queue.length - 1) playTrack(queue[idx + 1], queue);
    }, [currentTrack, queue, playTrack]);

    const previous = useCallback(() => {
        if (!currentTrack || queue.length === 0) return;
        const idx = queue.findIndex(t => t.id === currentTrack.id);
        if (idx > 0) playTrack(queue[idx - 1], queue);
    }, [currentTrack, queue, playTrack]);

    return (
        <PlayerContext.Provider value={{
            currentTrack, isPlaying, currentTime, duration, volume, queue,
            play: playTrack, pause, resume, stop, seek, setVolume, next, previous
        }}>
            {children}
        </PlayerContext.Provider>
    );
};
