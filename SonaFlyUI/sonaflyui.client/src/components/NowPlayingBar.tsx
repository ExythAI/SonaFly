import React, { useState } from 'react';
import { Box, Typography, IconButton, Slider } from '@mui/material';
import {
    PlayArrow, Pause, Stop, SkipNext, SkipPrevious, VolumeUp, VolumeOff,
    KeyboardArrowDown, KeyboardArrowUp
} from '@mui/icons-material';
import { usePlayer } from './PlayerContext';
import { artworkUrl } from '../api/client';

const PLAYER_HEIGHT = 72;
const MINI_PLAYER_HEIGHT = 36;

const fmt = (s: number) => {
    if (!s || isNaN(s)) return '0:00';
    const m = Math.floor(s / 60);
    return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
};

const NowPlayingBar: React.FC = () => {
    const {
        currentTrack, isPlaying, currentTime, duration, volume,
        pause, resume, stop, seek, setVolume, next, previous, queue
    } = usePlayer();
    const [minimized, setMinimized] = useState(false);

    if (!currentTrack) return null;

    const currentIdx = queue.findIndex(t => t.id === currentTrack.id);
    const hasPrev = currentIdx > 0;
    const hasNext = currentIdx >= 0 && currentIdx < queue.length - 1;
    const progress = duration ? (currentTime / duration) * 100 : 0;

    // Mini player — thin bar with progress, track name, play/pause, expand
    if (minimized) {
        return (
            <Box sx={{
                height: MINI_PLAYER_HEIGHT, flexShrink: 0,
                bgcolor: 'rgba(18,18,24,0.95)',
                borderTop: '1px solid rgba(255,255,255,0.06)',
                display: 'flex', alignItems: 'center', px: 1.5, gap: 1,
                position: 'relative',
            }}>
                {/* Progress bar at top edge */}
                <Box sx={{ position: 'absolute', top: 0, left: 0, right: 0, height: 2, bgcolor: 'rgba(255,255,255,0.06)' }}>
                    <Box sx={{ height: '100%', width: `${progress}%`, bgcolor: 'primary.main', transition: 'width 0.3s linear' }} />
                </Box>

                {/* Artwork tiny */}
                {currentTrack.artworkId && (
                    <Box sx={{
                        width: 24, height: 24, borderRadius: 0.5, overflow: 'hidden', flexShrink: 0,
                    }}>
                        <Box component="img" src={artworkUrl(currentTrack.artworkId)}
                            sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                    </Box>
                )}

                {/* Track info */}
                <Typography variant="caption" noWrap sx={{ color: '#E8EAED', flex: 1, minWidth: 0, fontSize: 12 }}>
                    {currentTrack.title}
                    <span style={{ color: '#9AA0A6' }}> · {currentTrack.artistName ?? 'Unknown'}</span>
                </Typography>

                {/* Mini controls */}
                <Typography variant="caption" sx={{ color: '#9AA0A6', fontSize: 11 }}>{fmt(currentTime)}</Typography>
                <IconButton size="small" onClick={isPlaying ? pause : resume} sx={{ p: 0.25 }}>
                    {isPlaying ? <Pause sx={{ fontSize: 18, color: '#E8EAED' }} /> : <PlayArrow sx={{ fontSize: 18, color: '#E8EAED' }} />}
                </IconButton>
                <IconButton size="small" onClick={() => setMinimized(false)} sx={{ p: 0.25 }} title="Expand player">
                    <KeyboardArrowUp sx={{ fontSize: 18, color: '#9AA0A6' }} />
                </IconButton>
            </Box>
        );
    }

    // Full player
    return (
        <Box sx={{
            height: PLAYER_HEIGHT, flexShrink: 0,
            borderTop: '1px solid rgba(255,255,255,0.08)',
            display: 'flex', alignItems: 'center', px: 2, gap: 2,
            backdropFilter: 'blur(20px)',
            background: 'linear-gradient(180deg, rgba(30,30,40,0.97) 0%, rgba(18,18,24,0.99) 100%)',
        }}>
            {/* Track info */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 200, flex: '0 0 auto' }}>
                <Box sx={{
                    width: 48, height: 48, borderRadius: 1.5, overflow: 'hidden',
                    bgcolor: 'rgba(124,77,255,0.15)', flexShrink: 0,
                    display: 'flex', alignItems: 'center', justifyContent: 'center'
                }}>
                    {currentTrack.artworkId ? (
                        <Box component="img" src={artworkUrl(currentTrack.artworkId)}
                            sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                    ) : (
                        <Typography sx={{ color: 'rgba(255,255,255,0.2)', fontSize: 20 }}>♪</Typography>
                    )}
                </Box>
                <Box sx={{ minWidth: 0 }}>
                    <Typography variant="body2" fontWeight={600} noWrap sx={{ color: '#E8EAED' }}>
                        {currentTrack.title}
                    </Typography>
                    <Typography variant="caption" noWrap sx={{ color: '#9AA0A6' }}>
                        {currentTrack.artistName ?? 'Unknown artist'}
                        {currentTrack.albumTitle ? ` · ${currentTrack.albumTitle}` : ''}
                    </Typography>
                </Box>
            </Box>

            {/* Playback controls */}
            <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flex: 1, minWidth: 0 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                    <IconButton size="small" onClick={previous} disabled={!hasPrev}
                        sx={{ color: hasPrev ? '#E8EAED' : 'rgba(255,255,255,0.2)' }}>
                        <SkipPrevious fontSize="small" />
                    </IconButton>
                    <IconButton onClick={isPlaying ? pause : resume}
                        sx={{
                            bgcolor: 'primary.main', color: '#fff', mx: 0.5,
                            width: 40, height: 40,
                            '&:hover': { bgcolor: 'primary.light' }
                        }}>
                        {isPlaying ? <Pause /> : <PlayArrow />}
                    </IconButton>
                    <IconButton size="small" onClick={next} disabled={!hasNext}
                        sx={{ color: hasNext ? '#E8EAED' : 'rgba(255,255,255,0.2)' }}>
                        <SkipNext fontSize="small" />
                    </IconButton>
                    <IconButton size="small" onClick={stop} sx={{ color: '#9AA0A6', ml: 0.5 }}>
                        <Stop fontSize="small" />
                    </IconButton>
                </Box>
                <Box sx={{ display: 'flex', alignItems: 'center', width: '100%', maxWidth: 600, gap: 1 }}>
                    <Typography variant="caption" sx={{ color: '#9AA0A6', minWidth: 36, textAlign: 'right' }}>
                        {fmt(currentTime)}
                    </Typography>
                    <Slider
                        size="small"
                        value={currentTime}
                        max={duration || 1}
                        onChange={(_, v) => seek(v as number)}
                        sx={{
                            color: 'primary.main', height: 4,
                            '& .MuiSlider-thumb': { width: 12, height: 12 },
                            '& .MuiSlider-rail': { bgcolor: 'rgba(255,255,255,0.08)' }
                        }}
                    />
                    <Typography variant="caption" sx={{ color: '#9AA0A6', minWidth: 36 }}>
                        {fmt(duration)}
                    </Typography>
                </Box>
            </Box>

            {/* Volume + Minimize */}
            <Box sx={{ display: { xs: 'none', sm: 'flex' }, alignItems: 'center', gap: 0.5, minWidth: 140 }}>
                <IconButton size="small" onClick={() => setVolume(volume === 0 ? 1 : 0)}
                    sx={{ color: '#9AA0A6' }}>
                    {volume === 0 ? <VolumeOff fontSize="small" /> : <VolumeUp fontSize="small" />}
                </IconButton>
                <Slider
                    size="small"
                    value={volume}
                    min={0} max={1} step={0.01}
                    onChange={(_, v) => setVolume(v as number)}
                    sx={{
                        color: 'primary.main', width: 100, height: 4,
                        '& .MuiSlider-thumb': { width: 10, height: 10 },
                        '& .MuiSlider-rail': { bgcolor: 'rgba(255,255,255,0.08)' }
                    }}
                />
            </Box>
            <IconButton size="small" onClick={() => setMinimized(true)} sx={{ color: '#9AA0A6', ml: -1 }} title="Minimize player">
                <KeyboardArrowDown fontSize="small" />
            </IconButton>
        </Box>
    );
};

export { NowPlayingBar, PLAYER_HEIGHT, MINI_PLAYER_HEIGHT };
