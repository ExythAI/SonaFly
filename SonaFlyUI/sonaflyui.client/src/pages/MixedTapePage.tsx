import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, Button, TextField, Dialog, DialogTitle,
    DialogContent, DialogActions, IconButton, CircularProgress, Grid, Chip,
    LinearProgress, InputAdornment, Divider, CardActionArea, Tooltip
} from '@mui/material';
import {
    Add, Delete, Close, Search, PlayArrow, Remove, Album as AlbumIcon, AccessTime
} from '@mui/icons-material';
import { mixedTapesApi, browseApi, artworkUrl } from '../api/client';
import { usePlayer } from '../components/PlayerContext';

const MAX_DURATION = 3600; // 60 minutes in seconds

const fmt = (s: number) => {
    if (!s || isNaN(s)) return '0:00';
    const m = Math.floor(s / 60);
    return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
};

const fmtLong = (s: number) => {
    const m = Math.floor(s / 60);
    const sec = Math.floor(s % 60);
    return `${m}m ${sec}s`;
};

// ─── List View ───────────────────────────────────────────────
const TapeListView: React.FC<{ onSelect: (id: string) => void; onCreate: () => void }> = ({ onSelect, onCreate }) => {
    const { data: tapes, isLoading } = useQuery({
        queryKey: ['mixed-tapes'],
        queryFn: () => mixedTapesApi.getAll().then(r => r.data),
    });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
                <Box>
                    <Typography variant="h4" sx={{
                        fontWeight: 800,
                        background: 'linear-gradient(135deg, #FF6B6B, #FFE66D, #4ECDC4)',
                        WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent'
                    }}>Mixed Tapes</Typography>
                    <Typography variant="body2" color="text.secondary">Curated 60-minute song packs</Typography>
                </Box>
                <Button variant="contained" startIcon={<Add />} onClick={onCreate}
                    sx={{
                        background: 'linear-gradient(135deg, #FF6B6B 0%, #FFE66D 100%)',
                        color: '#1a1a2e', fontWeight: 700,
                        '&:hover': { background: 'linear-gradient(135deg, #ff5252 0%, #ffd740 100%)' }
                    }}>
                    New Mixed Tape
                </Button>
            </Box>

            {(!tapes || tapes.length === 0) && (
                <Card sx={{ textAlign: 'center', py: 8, background: 'linear-gradient(135deg, rgba(255,107,107,0.05), rgba(78,205,196,0.05))' }}>
                    <CardContent>
                        <Box component="img" src="/mixedtape-200.png" alt="Mixed Tape" sx={{ width: 200, height: 200, mb: 2, opacity: 0.3 }} />
                        <Typography variant="h6" color="text.secondary">No mixed tapes yet</Typography>
                        <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                            Create your first 60-minute curated playlist
                        </Typography>
                        <Button variant="outlined" startIcon={<Add />} onClick={onCreate}>Create One</Button>
                    </CardContent>
                </Card>
            )}

            <Grid container spacing={2}>
                {tapes?.map((tape: any) => {
                    const pct = Math.min(100, (tape.totalDurationSeconds / tape.targetDurationSeconds) * 100);
                    return (
                        <Grid key={tape.id} size={{ xs: 12, sm: 6, md: 4 }}>
                            <Card sx={{
                                background: 'linear-gradient(145deg, rgba(255,107,107,0.06) 0%, rgba(78,205,196,0.06) 100%)',
                                border: '1px solid rgba(255,255,255,0.06)',
                                transition: 'all 0.2s', '&:hover': { border: '1px solid rgba(255,107,107,0.3)', transform: 'translateY(-2px)' }
                            }}>
                                <CardActionArea onClick={() => onSelect(tape.id)}>
                                    <CardContent>
                                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
                                            <Box component="img" src="/mixedtape-48.png" alt="" sx={{ width: 48, height: 48 }} />
                                            <Box sx={{ flex: 1, minWidth: 0 }}>
                                                <Typography variant="h6" fontWeight={700} noWrap>{tape.name}</Typography>
                                                <Typography variant="caption" color="text.secondary">
                                                    {tape.trackCount} tracks · {fmtLong(tape.totalDurationSeconds)}
                                                </Typography>
                                            </Box>
                                        </Box>
                                        <Box sx={{ mb: 0.5, display: 'flex', justifyContent: 'space-between' }}>
                                            <Typography variant="caption" sx={{ color: pct >= 95 ? '#4ECDC4' : '#9AA0A6' }}>
                                                {pct >= 95 ? '✓ Complete' : `${fmtLong(tape.remainingSeconds)} remaining`}
                                            </Typography>
                                            <Typography variant="caption" color="text.secondary">{Math.round(pct)}%</Typography>
                                        </Box>
                                        <LinearProgress
                                            variant="determinate" value={pct}
                                            sx={{
                                                height: 6, borderRadius: 3,
                                                bgcolor: 'rgba(255,255,255,0.06)',
                                                '& .MuiLinearProgress-bar': {
                                                    borderRadius: 3,
                                                    background: pct >= 95
                                                        ? 'linear-gradient(90deg, #4ECDC4, #44E5A0)'
                                                        : 'linear-gradient(90deg, #FF6B6B, #FFE66D)',
                                                }
                                            }}
                                        />
                                    </CardContent>
                                </CardActionArea>
                            </Card>
                        </Grid>
                    );
                })}
            </Grid>
        </Box>
    );
};

// ─── Track Browser Panel ─────────────────────────────────────
const TrackBrowser: React.FC<{
    remainingSeconds: number;
    existingTrackIds: Set<string>;
    onAddTrack: (trackId: string) => void;
}> = ({ remainingSeconds, existingTrackIds, onAddTrack }) => {
    const [search, setSearch] = useState('');
    const [browseMode, setBrowseMode] = useState<'search' | 'albums'>('albums');
    const [selectedAlbum, setSelectedAlbum] = useState<string | null>(null);

    const { data: albums } = useQuery({
        queryKey: ['albums-browse'],
        queryFn: () => browseApi.albums(1, 200).then(r => r.data),
        enabled: browseMode === 'albums' && !selectedAlbum,
    });

    const { data: albumDetail } = useQuery({
        queryKey: ['album-detail', selectedAlbum],
        queryFn: () => browseApi.albumById(selectedAlbum!).then(r => r.data),
        enabled: !!selectedAlbum,
    });

    const { data: searchResults } = useQuery({
        queryKey: ['track-search', search],
        queryFn: () => browseApi.search(search, 50).then(r => r.data),
        enabled: browseMode === 'search' && search.length >= 2,
    });

    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
            {/* Search bar */}
            <TextField
                size="small" fullWidth placeholder="Search tracks, albums..."
                value={search}
                onChange={e => { setSearch(e.target.value); if (e.target.value.length >= 2) setBrowseMode('search'); }}
                slotProps={{
                    input: {
                        startAdornment: <InputAdornment position="start"><Search sx={{ fontSize: 18, color: '#9AA0A6' }} /></InputAdornment>,
                        endAdornment: search && (
                            <InputAdornment position="end">
                                <IconButton size="small" onClick={() => { setSearch(''); setBrowseMode('albums'); setSelectedAlbum(null); }}>
                                    <Close sx={{ fontSize: 16 }} />
                                </IconButton>
                            </InputAdornment>
                        )
                    }
                }}
                sx={{ mb: 1.5, '.MuiOutlinedInput-root': { bgcolor: 'rgba(255,255,255,0.03)' } }}
            />

            {/* Browse tabs */}
            {!selectedAlbum && browseMode !== 'search' && (
                <Box sx={{ display: 'flex', gap: 1, mb: 1.5 }}>
                    <Chip label="Albums" size="small" onClick={() => { setBrowseMode('albums'); setSearch(''); }}
                        sx={{ bgcolor: browseMode === 'albums' ? 'primary.main' : 'rgba(255,255,255,0.06)', color: browseMode === 'albums' ? '#fff' : '#9AA0A6' }} />
                </Box>
            )}

            {/* Album grid browse */}
            {browseMode === 'albums' && !selectedAlbum && (
                <Box sx={{ flex: 1, overflowY: 'auto', overflowX: 'hidden' }}>
                    <Grid container spacing={1}>
                        {albums?.items?.map((a: any) => (
                            <Grid key={a.id} size={{ xs: 4, sm: 4 }}>
                                <Card sx={{
                                    bgcolor: 'rgba(255,255,255,0.03)', cursor: 'pointer',
                                    transition: 'all 0.15s', '&:hover': { bgcolor: 'rgba(255,255,255,0.06)' }
                                }}>
                                    <CardActionArea onClick={() => setSelectedAlbum(a.id)}>
                                        <Box sx={{ pt: '100%', position: 'relative', bgcolor: 'rgba(124,77,255,0.08)' }}>
                                            {a.artworkId && <Box component="img" src={artworkUrl(a.artworkId)} sx={{ position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', objectFit: 'cover' }} />}
                                            {!a.artworkId && <Box sx={{ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%,-50%)' }}><AlbumIcon sx={{ color: 'rgba(255,255,255,0.1)', fontSize: 28 }} /></Box>}
                                        </Box>
                                        <CardContent sx={{ py: 1, px: 1 }}>
                                            <Typography variant="caption" fontWeight={600} noWrap display="block">{a.title}</Typography>
                                            <Typography variant="caption" color="text.secondary" noWrap display="block" sx={{ fontSize: 10 }}>{a.artistName ?? 'Various'}</Typography>
                                        </CardContent>
                                    </CardActionArea>
                                </Card>
                            </Grid>
                        ))}
                    </Grid>
                </Box>
            )}

            {/* Album detail track list */}
            {selectedAlbum && albumDetail && (
                <Box sx={{ flex: 1, overflowY: 'auto' }}>
                    <Button size="small" onClick={() => setSelectedAlbum(null)}
                        sx={{ mb: 1, color: '#FFE66D', textTransform: 'none', fontWeight: 600, '&:hover': { bgcolor: 'rgba(255,230,109,0.08)' } }}>
                        ← Back to Albums
                    </Button>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
                        {albumDetail.artworkId && <Box component="img" src={artworkUrl(albumDetail.artworkId)} sx={{ width: 36, height: 36, borderRadius: 1, objectFit: 'cover' }} />}
                        <Box sx={{ minWidth: 0 }}>
                            <Typography variant="body2" fontWeight={700} noWrap>{albumDetail.title}</Typography>
                            <Typography variant="caption" color="text.secondary" noWrap>{albumDetail.artistName}</Typography>
                        </Box>
                    </Box>
                    {albumDetail.tracks?.map((t: any) => {
                        const tooLong = (t.durationSeconds ?? 0) > remainingSeconds;
                        const alreadyAdded = existingTrackIds.has(t.id);
                        const disabled = tooLong || alreadyAdded;
                        return (
                            <Box key={t.id} sx={{
                                display: 'flex', alignItems: 'center', gap: 1, py: 0.75, px: 1,
                                borderRadius: 1, opacity: disabled ? 0.35 : 1,
                                transition: 'all 0.15s',
                                ...(disabled ? {} : { cursor: 'pointer', '&:hover': { bgcolor: 'rgba(255,107,107,0.08)' } }),
                            }} onClick={() => !disabled && onAddTrack(t.id)}>
                                <Typography variant="caption" sx={{ color: '#9AA0A6', width: 24, textAlign: 'right', flexShrink: 0 }}>
                                    {t.trackNumber ?? '·'}
                                </Typography>
                                <Box sx={{ flex: 1, minWidth: 0 }}>
                                    <Typography variant="body2" fontWeight={500} noWrap sx={{ color: alreadyAdded ? '#4ECDC4' : '#E8EAED' }}>
                                        {t.title} {alreadyAdded && '✓'}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" noWrap>{t.artistName ?? '—'}</Typography>
                                </Box>
                                <Typography variant="caption" sx={{ color: tooLong ? '#FF6B6B' : '#9AA0A6', flexShrink: 0 }}>
                                    {fmt(t.durationSeconds)}
                                    {tooLong && !alreadyAdded && ' ✕'}
                                </Typography>
                                {!disabled && (
                                    <Tooltip title="Add to tape"><Add sx={{ fontSize: 18, color: '#FFE66D', flexShrink: 0 }} /></Tooltip>
                                )}
                            </Box>
                        );
                    })}
                </Box>
            )}

            {/* Search results */}
            {browseMode === 'search' && search.length >= 2 && (
                <Box sx={{ flex: 1, overflowY: 'auto' }}>
                    {searchResults?.tracks?.length > 0 ? searchResults.tracks.map((t: any) => {
                        const tooLong = (t.durationSeconds ?? 0) > remainingSeconds;
                        const alreadyAdded = existingTrackIds.has(t.id);
                        const disabled = tooLong || alreadyAdded;
                        return (
                            <Box key={t.id} sx={{
                                display: 'flex', alignItems: 'center', gap: 1, py: 0.75, px: 1,
                                borderRadius: 1, opacity: disabled ? 0.35 : 1,
                                ...(disabled ? {} : { cursor: 'pointer', '&:hover': { bgcolor: 'rgba(255,107,107,0.08)' } }),
                            }} onClick={() => !disabled && onAddTrack(t.id)}>
                                {t.artworkId && <Box component="img" src={artworkUrl(t.artworkId)} sx={{ width: 32, height: 32, borderRadius: 0.5, objectFit: 'cover', flexShrink: 0 }} />}
                                <Box sx={{ flex: 1, minWidth: 0 }}>
                                    <Typography variant="body2" fontWeight={500} noWrap sx={{ color: alreadyAdded ? '#4ECDC4' : '#E8EAED' }}>
                                        {t.title} {alreadyAdded && '✓'}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" noWrap>{t.artistName ?? '—'} · {t.albumTitle ?? '—'}</Typography>
                                </Box>
                                <Typography variant="caption" sx={{ color: tooLong ? '#FF6B6B' : '#9AA0A6', flexShrink: 0 }}>{fmt(t.durationSeconds)}</Typography>
                                {!disabled && <Tooltip title="Add to tape"><Add sx={{ fontSize: 18, color: '#FFE66D', flexShrink: 0 }} /></Tooltip>}
                            </Box>
                        );
                    }) : (
                        <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ py: 4 }}>
                            {search.length >= 2 ? 'No tracks found' : 'Type to search...'}
                        </Typography>
                    )}
                </Box>
            )}
        </Box>
    );
};

// ─── Detail / Edit View ──────────────────────────────────────
const TapeDetailView: React.FC<{ tapeId: string; onBack: () => void }> = ({ tapeId, onBack }) => {
    const qc = useQueryClient();
    const { play } = usePlayer();
    const { data: tape, isLoading } = useQuery({
        queryKey: ['mixed-tape', tapeId],
        queryFn: () => mixedTapesApi.getById(tapeId).then(r => r.data),
    });

    const addMut = useMutation({
        mutationFn: (trackId: string) => mixedTapesApi.addTrack(tapeId, trackId),
        onSuccess: () => qc.invalidateQueries({ queryKey: ['mixed-tape', tapeId] }),
    });

    const removeMut = useMutation({
        mutationFn: (itemId: string) => mixedTapesApi.removeItem(tapeId, itemId),
        onSuccess: () => qc.invalidateQueries({ queryKey: ['mixed-tape', tapeId] }),
    });

    const deleteMut = useMutation({
        mutationFn: () => mixedTapesApi.delete(tapeId),
        onSuccess: () => { qc.invalidateQueries({ queryKey: ['mixed-tapes'] }); onBack(); },
    });

    if (isLoading || !tape) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    const pct = Math.min(100, (tape.totalDurationSeconds / tape.targetDurationSeconds) * 100);
    const remaining = Math.max(0, tape.targetDurationSeconds - tape.totalDurationSeconds);
    const existingTrackIds = new Set<string>(tape.items?.map((i: any) => i.trackId) ?? []);

    const handlePlayAll = () => {
        if (tape.items?.length > 0) {
            const queue = tape.items.map((i: any) => ({
                id: i.trackId,
                title: i.trackTitle,
                artistName: i.artistName,
                albumTitle: i.albumTitle,
                artworkId: i.artworkId,
                durationSeconds: i.durationSeconds,
            }));
            play(queue[0], queue);
        }
    };

    return (
        <Box>
            {/* Header */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 3 }}>
                <IconButton onClick={onBack}><Close /></IconButton>
                <Typography variant="h5" fontWeight={800} sx={{
                    flex: 1,
                    background: 'linear-gradient(135deg, #FF6B6B, #FFE66D)',
                    WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent'
                }}>
                    <Box component="img" src="/mixedtape-32.png" alt="" sx={{ width: 32, height: 32, mr: 0.5, display: 'inline-block', verticalAlign: 'middle' }} />{' '}{tape.name}
                </Typography>
                <Button size="small" variant="contained" startIcon={<PlayArrow />} onClick={handlePlayAll}
                    disabled={tape.items?.length === 0}
                    sx={{
                        background: 'linear-gradient(135deg, #4ECDC4, #44E5A0)',
                        color: '#1a1a2e', fontWeight: 700,
                        '&:hover': { background: 'linear-gradient(135deg, #45b7aa, #3dcc8f)' }
                    }}>
                    Play
                </Button>
                <IconButton size="small" onClick={() => deleteMut.mutate()} color="error" title="Delete tape"><Delete fontSize="small" /></IconButton>
            </Box>

            {/* Duration bar */}
            <Card sx={{
                mb: 3, p: 2,
                background: 'linear-gradient(135deg, rgba(255,107,107,0.06) 0%, rgba(78,205,196,0.06) 100%)',
                border: '1px solid rgba(255,255,255,0.06)'
            }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                        <AccessTime sx={{ fontSize: 18, color: '#FFE66D' }} />
                        <Typography variant="body2" fontWeight={600}>{fmtLong(tape.totalDurationSeconds)} filled</Typography>
                    </Box>
                    <Typography variant="body2" sx={{ color: remaining < 120 ? '#4ECDC4' : '#FFE66D' }}>
                        {remaining > 0 ? `${fmtLong(remaining)} remaining` : '✓ Full'}
                    </Typography>
                </Box>
                <LinearProgress
                    variant="determinate" value={pct}
                    sx={{
                        height: 10, borderRadius: 5, bgcolor: 'rgba(255,255,255,0.06)',
                        '& .MuiLinearProgress-bar': {
                            borderRadius: 5,
                            background: pct >= 95
                                ? 'linear-gradient(90deg, #4ECDC4, #44E5A0)'
                                : 'linear-gradient(90deg, #FF6B6B, #FFE66D, #4ECDC4)',
                            transition: 'width 0.5s ease'
                        }
                    }}
                />
                <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 0.5 }}>
                    <Typography variant="caption" color="text.secondary">0:00</Typography>
                    <Typography variant="caption" color="text.secondary">{tape.items?.length ?? 0} tracks</Typography>
                    <Typography variant="caption" color="text.secondary">60:00</Typography>
                </Box>
            </Card>

            {/* Two-panel layout */}
            <Grid container spacing={2}>
                {/* Left: Tape contents */}
                <Grid size={{ xs: 12, md: 5 }}>
                    <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>
                        Tape Contents
                    </Typography>
                    <Card sx={{ bgcolor: 'rgba(255,255,255,0.02)', border: '1px solid rgba(255,255,255,0.06)' }}>
                        {tape.items?.length === 0 ? (
                            <Box sx={{ py: 6, textAlign: 'center' }}>
                                <Box component="img" src="/mixedtape-80.png" alt="" sx={{ width: 80, height: 80, mb: 1, opacity: 0.3 }} />
                                <Typography variant="body2" color="text.secondary">Empty tape</Typography>
                                <Typography variant="caption" color="text.secondary">Browse albums on the right to add tracks</Typography>
                            </Box>
                        ) : (
                            <Box>
                                {tape.items.map((item: any, i: number) => (
                                    <Box key={item.id} sx={{
                                        display: 'flex', alignItems: 'center', gap: 1, py: 1, px: 1.5,
                                        borderBottom: i < tape.items.length - 1 ? '1px solid rgba(255,255,255,0.04)' : 'none',
                                        '&:hover': { bgcolor: 'rgba(255,255,255,0.03)' },
                                        '&:hover .remove-btn': { opacity: 1 }
                                    }}>
                                        <Typography variant="caption" sx={{ color: '#9AA0A6', width: 20, textAlign: 'right', flexShrink: 0 }}>
                                            {i + 1}
                                        </Typography>
                                        {item.artworkId && (
                                            <Box component="img" src={artworkUrl(item.artworkId)}
                                                sx={{ width: 32, height: 32, borderRadius: 0.5, objectFit: 'cover', flexShrink: 0 }} />
                                        )}
                                        <Box sx={{ flex: 1, minWidth: 0 }}>
                                            <Typography variant="body2" fontWeight={500} noWrap>{item.trackTitle}</Typography>
                                            <Typography variant="caption" color="text.secondary" noWrap>
                                                {item.artistName ?? '—'} · {item.albumTitle ?? '—'}
                                            </Typography>
                                        </Box>
                                        <Typography variant="caption" sx={{ color: '#9AA0A6', flexShrink: 0 }}>{fmt(item.durationSeconds)}</Typography>
                                        <IconButton className="remove-btn" size="small" onClick={() => removeMut.mutate(item.id)}
                                            sx={{ opacity: 0, transition: 'opacity 0.15s', color: '#FF6B6B' }}>
                                            <Remove sx={{ fontSize: 16 }} />
                                        </IconButton>
                                    </Box>
                                ))}
                            </Box>
                        )}
                    </Card>
                </Grid>

                {/* Right: Track browser */}
                <Grid size={{ xs: 12, md: 7 }}>
                    <Typography variant="overline" color="text.secondary" sx={{ mb: 1, display: 'block' }}>
                        Browse Library
                    </Typography>
                    <Card sx={{
                        bgcolor: 'rgba(255,255,255,0.02)', border: '1px solid rgba(255,255,255,0.06)',
                        p: 1.5, minHeight: 400, display: 'flex', flexDirection: 'column'
                    }}>
                        <TrackBrowser
                            remainingSeconds={remaining}
                            existingTrackIds={existingTrackIds}
                            onAddTrack={(trackId) => addMut.mutate(trackId)}
                        />
                    </Card>
                </Grid>
            </Grid>
        </Box>
    );
};

// ─── Main Page ───────────────────────────────────────────────
const MixedTapePage: React.FC = () => {
    const qc = useQueryClient();
    const [view, setView] = useState<'list' | 'detail'>('list');
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [createOpen, setCreateOpen] = useState(false);
    const [newName, setNewName] = useState('');

    const createMut = useMutation({
        mutationFn: () => mixedTapesApi.create({ name: newName }),
        onSuccess: (res) => {
            qc.invalidateQueries({ queryKey: ['mixed-tapes'] });
            setCreateOpen(false);
            setNewName('');
            setSelectedId(res.data.id);
            setView('detail');
        },
    });

    return (
        <Box>
            {view === 'list' && (
                <TapeListView
                    onSelect={(id) => { setSelectedId(id); setView('detail'); }}
                    onCreate={() => setCreateOpen(true)}
                />
            )}

            {view === 'detail' && selectedId && (
                <TapeDetailView
                    tapeId={selectedId}
                    onBack={() => { setView('list'); setSelectedId(null); }}
                />
            )}

            {/* Create Dialog */}
            <Dialog open={createOpen} onClose={() => setCreateOpen(false)} maxWidth="sm" fullWidth
                PaperProps={{ sx: { background: 'linear-gradient(135deg, rgba(30,30,40,0.98), rgba(18,18,24,0.99))' } }}>
                <DialogTitle sx={{
                    fontWeight: 800,
                    background: 'linear-gradient(135deg, #FF6B6B, #FFE66D)',
                    WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent'
                }}>
                    <Box component="img" src="/mixedtape-32.png" alt="" sx={{ width: 32, height: 32, mr: 0.5, display: 'inline-block', verticalAlign: 'middle' }} />{' '}New Mixed Tape
                </DialogTitle>
                <DialogContent sx={{ pt: '16px !important' }}>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                        Give your 60-minute mixtape a name
                    </Typography>
                    <TextField
                        fullWidth autoFocus label="Tape Name" value={newName}
                        onChange={e => setNewName(e.target.value)}
                        placeholder="Friday Night Vibes"
                        onKeyDown={e => { if (e.key === 'Enter' && newName.trim()) createMut.mutate(); }}
                    />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setCreateOpen(false)}>Cancel</Button>
                    <Button variant="contained" onClick={() => createMut.mutate()} disabled={!newName.trim()}
                        sx={{
                            background: 'linear-gradient(135deg, #FF6B6B, #FFE66D)',
                            color: '#1a1a2e', fontWeight: 700,
                        }}>
                        Create
                    </Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default MixedTapePage;
