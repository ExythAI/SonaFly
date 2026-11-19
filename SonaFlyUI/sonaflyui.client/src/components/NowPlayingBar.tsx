import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Box, Typography, IconButton, Slider, Stack, Button, CircularProgress, Dialog, DialogTitle, DialogContent, List, ListItemButton, ListItemText, DialogActions, Tooltip } from '@mui/material';
import { PlayArrow, Pause, Stop, SkipNext, SkipPrevious, VolumeUp, VolumeOff, KeyboardArrowDown, KeyboardArrowUp, QueueMusic, MeetingRoom, Shuffle, Repeat, RepeatOne } from '@mui/icons-material';
import { usePlayer } from './PlayerContext';
import { Cover, FavoriteButton } from './Music';
export const PLAYER_HEIGHT = 96;
export const MINI_PLAYER_HEIGHT = 56;
const fmt = (seconds: number) => Number.isFinite(seconds) ? `${Math.floor(Math.max(0, seconds) / 60)}:${String(Math.floor(Math.max(0, seconds) % 60)).padStart(2, '0')}` : '0:00';
const REPEAT_LABEL = { off: 'Repeat is off', all: 'Repeating the queue', one: 'Repeating this track' } as const;
const linkSx = { color: 'inherit', textDecoration: 'none', '&:hover': { textDecoration: 'underline' } } as const;
export function NowPlayingBar() {
    const player = usePlayer();
    const { currentTrack, isPlaying, isBuffering, playbackError, currentTime, duration, volume, queue, currentIndex, sharedRoom, shuffle, repeat, hasNext } = player;
    const [minimized, setMinimized] = useState(false);
    const [queueOpen, setQueueOpen] = useState(false);
    const [drag, setDrag] = useState<{ id: string; value: number } | null>(null);
    const previousVolume = useRef(1);
    if (!currentTrack) return null;
    const controls = <Stack direction="row" alignItems="center" justifyContent="center" spacing={0.5}>
        {!sharedRoom && <Tooltip title={shuffle ? 'Shuffle is on' : 'Shuffle is off'}>
            <IconButton size="small" aria-label="Shuffle" aria-pressed={shuffle} onClick={player.toggleShuffle} sx={{ color: shuffle ? 'primary.light' : 'text.secondary' }}><Shuffle fontSize="small" /></IconButton>
        </Tooltip>}
        {!sharedRoom && <IconButton aria-label="Previous track" onClick={player.previous}><SkipPrevious /></IconButton>}
        {sharedRoom ? <Button component={Link} to="/auditoriums" startIcon={<MeetingRoom />} size="small">In {sharedRoom}</Button> :
            <Tooltip title="Space"><IconButton aria-label={isPlaying ? 'Pause' : 'Play'} onClick={isPlaying ? player.pause : player.resume} sx={{ bgcolor: 'primary.main', color: '#fff', width: 44, height: 44, '&:hover': { bgcolor: 'primary.dark' } }}>
                {isBuffering ? <CircularProgress size={20} color="inherit" /> : isPlaying ? <Pause /> : <PlayArrow />}
            </IconButton></Tooltip>}
        {!sharedRoom && <IconButton aria-label="Next track" onClick={player.next} disabled={!hasNext}><SkipNext /></IconButton>}
        {!sharedRoom && <Tooltip title={REPEAT_LABEL[repeat]}>
            <IconButton size="small" aria-label={`Repeat: ${REPEAT_LABEL[repeat]}`} onClick={player.cycleRepeat} sx={{ color: repeat === 'off' ? 'text.secondary' : 'primary.light' }}>
                {repeat === 'one' ? <RepeatOne fontSize="small" /> : <Repeat fontSize="small" />}
            </IconButton>
        </Tooltip>}
    </Stack>;
    return <Box component="section" aria-label="Music player" sx={{ flexShrink: 0, bgcolor: '#11151F', borderTop: '1px solid', borderColor: 'divider', pb: 'env(safe-area-inset-bottom)' }}>
        {playbackError && <Alert severity="error" action={<Button color="inherit" onClick={player.retry}>Retry</Button>} sx={{ borderRadius: 0 }}>{playbackError}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr) auto', md: 'minmax(160px, 1fr) minmax(260px, 2fr) minmax(140px, 1fr)' }, gap: { xs: 0.5, md: 2 }, px: 2, py: 1, alignItems: 'center' }}>
            <Stack direction="row" spacing={1.5} alignItems="center" minWidth={0}>
                <Cover artworkId={currentTrack.artworkId} size={160} kind="track" sx={{ width: minimized ? 32 : 48, borderRadius: 1.5 }} />
                <Box minWidth={0}>
                    <Typography variant="body2" fontWeight={600} noWrap>
                        {currentTrack.albumId ? <Box component={Link} to={`/albums/${currentTrack.albumId}`} sx={linkSx}>{currentTrack.title}</Box> : currentTrack.title}
                    </Typography>
                    <Typography variant="caption" color="text.secondary" noWrap component="div">
                        {isBuffering ? 'Buffering…' : currentTrack.artistId
                            ? <Box component={Link} to={`/artists/${currentTrack.artistId}`} sx={linkSx}>{currentTrack.artistName || 'Unknown artist'}</Box>
                            : currentTrack.artistName || 'Unknown artist'}
                    </Typography>
                </Box>
                {!sharedRoom && <Box sx={{ display: { xs: 'none', sm: 'block' } }}><FavoriteButton track={currentTrack} /></Box>}
            </Stack>
            <Box sx={{ display: { xs: 'none', md: 'block' } }}>{controls}</Box>
            <Stack direction="row" justifyContent="flex-end" alignItems="center">
                <Box sx={{ display: { xs: minimized ? 'block' : 'none', md: 'none' } }}>{!sharedRoom && <IconButton aria-label={isPlaying ? 'Pause' : 'Play'} onClick={isPlaying ? player.pause : player.resume}>{isBuffering ? <CircularProgress size={20} /> : isPlaying ? <Pause /> : <PlayArrow />}</IconButton>}</Box>
                {!sharedRoom && <IconButton aria-label="Open playback queue" onClick={() => setQueueOpen(true)}><QueueMusic /></IconButton>}
                <Box sx={{ display: { xs: 'none', lg: 'flex' }, alignItems: 'center', width: 120 }}>
                    <IconButton aria-label={volume === 0 ? 'Unmute' : 'Mute'} onClick={() => { if (volume > 0) { previousVolume.current = volume; player.setVolume(0); } else player.setVolume(previousVolume.current || 1); }}>{volume === 0 ? <VolumeOff fontSize="small" /> : <VolumeUp fontSize="small" />}</IconButton>
                    <Slider aria-label="Volume" size="small" value={volume} min={0} max={1} step={0.01} onChange={(_, value) => player.setVolume(value as number)} />
                </Box>
                <IconButton aria-label={minimized ? 'Expand player' : 'Minimize player'} onClick={() => setMinimized(!minimized)}>{minimized ? <KeyboardArrowUp /> : <KeyboardArrowDown />}</IconButton>
            </Stack>
            {!minimized && <>
                <Box sx={{ display: { xs: 'block', md: 'none' }, gridColumn: '1 / -1' }}>{controls}</Box>
                <Stack direction="row" alignItems="center" gap={1.5} sx={{ gridColumn: '1 / -1' }}>
                    <Typography variant="caption" color="text.secondary">{fmt(drag?.id === currentTrack.id ? drag.value : currentTime)}</Typography>
                    <Slider aria-label={sharedRoom ? 'Shared playback position' : 'Playback position'} getAriaValueText={fmt} size="small" disabled={!!sharedRoom || !duration} max={duration || 1}
                        value={Math.min(duration || 1, drag?.id === currentTrack.id ? drag.value : currentTime)} onChange={(_, value) => setDrag({ id: currentTrack.id, value: value as number })}
                        onChangeCommitted={(_, value) => { player.seek(value as number); setDrag(null); }} />
                    <Typography variant="caption" color="text.secondary">{fmt(duration)}</Typography>
                    {!sharedRoom && <IconButton aria-label="Stop playback" size="small" onClick={player.stop}><Stop fontSize="small" /></IconButton>}
                </Stack>
            </>}
        </Box>
        <Dialog open={queueOpen} onClose={() => setQueueOpen(false)} maxWidth="sm" fullWidth>
            <DialogTitle>Playback queue <Typography variant="body2" color="text.secondary">{queue.length} tracks{shuffle ? ' · shuffled' : ''}</Typography></DialogTitle>
            <DialogContent dividers><List>{queue.map((track, index) => <ListItemButton key={`${track.id}-${index}`} selected={index === currentIndex} onClick={() => player.playIndex(index)} aria-label={`Play ${track.title}`}>
                <Typography color="text.secondary" sx={{ width: 32 }}>{index + 1}</Typography><ListItemText primary={track.title} secondary={track.artistName} />{index === currentIndex && <MusicIndicator playing={isPlaying} />}
            </ListItemButton>)}</List></DialogContent><DialogActions><Button onClick={() => setQueueOpen(false)}>Close</Button></DialogActions>
        </Dialog>
    </Box>;
}
function MusicIndicator({ playing }: { playing: boolean }) { return <Typography color="primary.light" variant="caption">{playing ? 'Playing' : 'Selected'}</Typography>; }
