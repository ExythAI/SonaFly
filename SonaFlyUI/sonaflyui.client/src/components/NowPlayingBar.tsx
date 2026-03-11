import React from 'react';
import { Box, Typography, IconButton, Slider } from '@mui/material';
import {
    PlayArrow, Pause, Stop, SkipNext, SkipPrevious, VolumeUp, VolumeOff
} from '@mui/icons-material';
import { usePlayer } from './PlayerContext';
import { artworkUrl } from '../api/client';

const PLAYER_HEIGHT = 72;

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

    if (!currentTrack) return null;

    const currentIdx = queue.findIndex(t => t.id === currentTrack.id);
    const hasPrev = currentIdx > 0;
    const hasNext = currentIdx >= 0 && currentIdx < queue.length - 1;

    return (
        <Box sx={{
            position: 'fixed', bottom: 0, left: 0, right: 0, height: PLAYER_HEIGHT,
            bgcolor: 'background.paper',
            borderTop: '1px solid rgba(255,255,255,0.08)',
            display: 'flex', alignItems: 'center', px: 2, gap: 2,
            zIndex: 1300,
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

            {/* Volume */}
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
        </Box>
    );
};

export { NowPlayingBar, PLAYER_HEIGHT };
