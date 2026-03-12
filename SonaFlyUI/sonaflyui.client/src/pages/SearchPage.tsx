import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Box, Typography, TextField, InputAdornment, Card, CardContent, Grid, Chip, CircularProgress, CardActionArea, IconButton } from '@mui/material';
import { Search as SearchIcon, PlayArrow, Pause } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { browseApi, artworkUrl } from '../api/client';
import { usePlayer } from '../components/PlayerContext';

const SearchPage: React.FC = () => {
    const [query, setQuery] = useState('');
    const navigate = useNavigate();
    const { play, pause, currentTrack, isPlaying } = usePlayer();
    const { data, isLoading } = useQuery({
        queryKey: ['search', query],
        queryFn: () => browseApi.search(query, 20).then(r => r.data),
        enabled: query.length >= 2,
    });

    const fmt = (s?: number) => {
        if (!s) return '--';
        const m = Math.floor(s / 60);
        return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
    };

    const handlePlayTrack = (t: any) => {
        play({
            id: t.id,
            title: t.title,
            artistName: t.artistName,
            albumTitle: t.albumTitle,
            artworkId: t.artworkId,
            durationSeconds: t.durationSeconds,
        });
    };

    return (
        <Box>
            <Typography variant="h4" gutterBottom>Search</Typography>
            <TextField fullWidth placeholder="Search artists, albums, tracks..."
                value={query} onChange={e => setQuery(e.target.value)}
                slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> } }}
                sx={{ mb: 3, '& .MuiOutlinedInput-root': { borderRadius: 3, bgcolor: 'background.paper' } }} />

            {isLoading && <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}><CircularProgress /></Box>}

            {data && (
                <Grid container spacing={3}>
                    {data.artists?.length > 0 && (
                        <Grid size={{ xs: 12 }}>
                            <Typography variant="h6" gutterBottom>Artists <Chip size="small" label={data.artists.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            <Grid container spacing={1.5}>
                                {data.artists.map((a: any) => (
                                    <Grid key={a.id} size={{ xs: 12, sm: 6, md: 4 }}>
                                        <Card>
                                            <CardActionArea onClick={() => navigate(`/artists/${a.id}`)}>
                                                <CardContent sx={{ py: 1.5, display: 'flex', alignItems: 'center', gap: 1.5 }}>
                                                    <Box sx={{ width: 40, height: 40, borderRadius: '50%', bgcolor: 'rgba(124,77,255,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                                        <Typography sx={{ color: 'primary.light' }}>{a.name[0]}</Typography>
                                                    </Box>
                                                    <Box>
                                                        <Typography variant="subtitle2">{a.name}</Typography>
                                                        <Typography variant="caption" color="text.secondary">{a.albumCount} albums</Typography>
                                                    </Box>
                                                </CardContent>
                                            </CardActionArea>
                                        </Card>
                                    </Grid>
                                ))}
                            </Grid>
                        </Grid>
                    )}
                    {data.albums?.length > 0 && (
                        <Grid size={{ xs: 12 }}>
                            <Typography variant="h6" gutterBottom>Albums <Chip size="small" label={data.albums.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            <Grid container spacing={1.5}>
                                {data.albums.map((a: any) => (
                                    <Grid key={a.id} size={{ xs: 12, sm: 6, md: 4 }}>
                                        <Card>
                                            <CardActionArea onClick={() => navigate(`/albums/${a.id}`)}>
                                                <CardContent sx={{ py: 1.5, display: 'flex', alignItems: 'center', gap: 1.5 }}>
                                                    <Box sx={{
                                                        width: 48, height: 48, borderRadius: 1, overflow: 'hidden', flexShrink: 0,
                                                        bgcolor: 'rgba(0,229,255,0.08)', display: 'flex', alignItems: 'center', justifyContent: 'center'
                                                    }}>
                                                        {a.artworkId ? (
                                                            <Box component="img" src={artworkUrl(a.artworkId)} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                                                        ) : (
                                                            <Typography sx={{ color: 'rgba(255,255,255,0.15)' }}>♪</Typography>
                                                        )}
                                                    </Box>
                                                    <Box>
                                                        <Typography variant="subtitle2">{a.title}</Typography>
                                                        <Typography variant="caption" color="text.secondary">{a.artistName}{a.year ? ` · ${a.year}` : ''}</Typography>
                                                    </Box>
                                                </CardContent>
                                            </CardActionArea>
                                        </Card>
                                    </Grid>
                                ))}
                            </Grid>
                        </Grid>
                    )}
                    {data.tracks?.length > 0 && (
                        <Grid size={{ xs: 12 }}>
                            <Typography variant="h6" gutterBottom>Tracks <Chip size="small" label={data.tracks.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            {data.tracks.map((t: any) => {
                                const isCurrent = currentTrack?.id === t.id;
                                return (
                                    <Card key={t.id} sx={{ mb: 1, ...(isCurrent ? { border: '1px solid', borderColor: 'primary.main', bgcolor: 'rgba(124,77,255,0.08)' } : {}) }}>
                                        <CardContent sx={{ py: 1, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                            <Box
                                                sx={{ cursor: 'pointer', flex: 1 }}
                                                onClick={() => t.albumId ? navigate(`/albums/${t.albumId}`) : undefined}
                                            >
                                                <Typography variant="subtitle2" sx={{ color: isCurrent ? 'primary.light' : 'text.primary' }}>{t.title}</Typography>
                                                <Typography variant="caption" color="text.secondary">{t.artistName} · {t.albumTitle} · {fmt(t.durationSeconds)}</Typography>
                                            </Box>
                                            <IconButton size="small" onClick={() => isCurrent && isPlaying ? pause() : handlePlayTrack(t)}>
                                                {isCurrent && isPlaying ? (
                                                    <Pause fontSize="small" sx={{ color: 'primary.light' }} />
                                                ) : (
                                                    <PlayArrow fontSize="small" />
                                                )}
                                            </IconButton>
                                        </CardContent>
                                    </Card>
                                );
                            })}
                        </Grid>
                    )}
                    {data && !data.artists?.length && !data.albums?.length && !data.tracks?.length && query.length >= 2 && (
                        <Grid size={{ xs: 12 }}><Typography color="text.secondary" textAlign="center" sx={{ py: 4 }}>No results found for "{query}"</Typography></Grid>
                    )}
                </Grid>
            )}
        </Box>
    );
};

export default SearchPage;
