import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { useParams, useNavigate } from 'react-router-dom';
import {
    Box, Typography, Card, CardContent, CardActionArea, Grid, CircularProgress, IconButton, Chip
} from '@mui/material';
import { PlayArrow, Pause, ArrowBack } from '@mui/icons-material';
import { browseApi, artworkUrl } from '../api/client';
import { usePlayer } from '../components/PlayerContext';

const ArtistDetailPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const { play, pause, currentTrack, isPlaying } = usePlayer();

    const { data: artist, isLoading: artistLoading } = useQuery({
        queryKey: ['artist', id],
        queryFn: () => browseApi.artistById(id!).then(r => r.data),
        enabled: !!id,
    });
    const { data: albums, isLoading: albumsLoading } = useQuery({
        queryKey: ['albums', id],
        queryFn: () => browseApi.albums(1, 100, id).then(r => r.data),
        enabled: !!id,
    });
    const { data: tracks, isLoading: tracksLoading } = useQuery({
        queryKey: ['artist-tracks', id],
        queryFn: () => browseApi.tracks(1, 200, 'title', 'asc', '', id!).then(r => r.data),
        enabled: !!id,
    });

    const fmt = (s?: number) => {
        if (!s) return '--';
        const m = Math.floor(s / 60);
        return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
    };

    const handlePlayTrack = (t: any) => {
        const allTracks = tracks?.items?.map((tr: any) => ({
            id: tr.id, title: tr.title, artistName: tr.artistName,
            albumTitle: tr.albumTitle, artworkId: tr.artworkId, durationSeconds: tr.durationSeconds,
        })) ?? [];
        play({
            id: t.id, title: t.title, artistName: t.artistName,
            albumTitle: t.albumTitle, artworkId: t.artworkId, durationSeconds: t.durationSeconds,
        }, allTracks);
    };

    if (artistLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;
    if (!artist) return <Typography>Artist not found</Typography>;

    const hasAlbums = albums?.items?.length > 0;
    const hasTracks = tracks?.items?.length > 0;

    return (
        <Box>
            {/* Header */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
                <IconButton onClick={() => navigate(-1)} sx={{ color: 'text.secondary' }}>
                    <ArrowBack />
                </IconButton>
                <Box sx={{
                    width: 72, height: 72, borderRadius: '50%', flexShrink: 0,
                    bgcolor: 'rgba(124,77,255,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center'
                }}>
                    <Typography variant="h4" sx={{ color: 'primary.light' }}>{artist.name[0]}</Typography>
                </Box>
                <Box>
                    <Typography variant="h4">{artist.name}</Typography>
                    <Typography variant="body2" color="text.secondary">
                        {artist.albumCount} {artist.albumCount === 1 ? 'album' : 'albums'} · {artist.trackCount} {artist.trackCount === 1 ? 'track' : 'tracks'}
                    </Typography>
                </Box>
            </Box>

            {/* Albums Section */}
            {hasAlbums && (
                <Box sx={{ mb: 4 }}>
                    <Typography variant="h6" sx={{ mb: 2 }}>
                        Albums <Chip size="small" label={albums.items.length} sx={{ ml: 1, height: 20 }} />
                    </Typography>
                    <Grid container spacing={2}>
                        {albums.items.map((a: any) => (
                            <Grid key={a.id} size={{ xs: 6, sm: 4, md: 3, lg: 2 }}>
                                <Card><CardActionArea onClick={() => navigate(`/albums/${a.id}`)}>
                                    <Box sx={{ pt: '100%', position: 'relative', bgcolor: 'rgba(0,229,255,0.08)' }}>
                                        {a.artworkId && <Box component="img" src={artworkUrl(a.artworkId)} sx={{ position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', objectFit: 'cover' }} />}
                                        {!a.artworkId && <Box sx={{ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%,-50%)' }}><Typography variant="h4" sx={{ color: 'rgba(255,255,255,0.1)' }}>♪</Typography></Box>}
                                    </Box>
                                    <CardContent sx={{ py: 1.5 }}>
                                        <Typography variant="subtitle2" noWrap>{a.title}</Typography>
                                        <Typography variant="caption" color="text.secondary" noWrap>{a.year ?? ''} · {a.trackCount} tracks</Typography>
                                    </CardContent>
                                </CardActionArea></Card>
                            </Grid>
                        ))}
                    </Grid>
                </Box>
            )}

            {/* Tracks Section */}
            {hasTracks && (
                <Box>
                    <Typography variant="h6" sx={{ mb: 2 }}>
                        Tracks <Chip size="small" label={tracks.totalCount} sx={{ ml: 1, height: 20 }} />
                    </Typography>
                    <Card>
                        <Box sx={{ overflowX: 'auto' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}>
                                        <th style={{ width: 50, padding: '12px 8px' }}></th>
                                        <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Title</th>
                                        <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Album</th>
                                        <th style={{ textAlign: 'right', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Duration</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {tracks.items.map((t: any) => {
                                        const isCurrent = currentTrack?.id === t.id;
                                        return (
                                            <tr key={t.id} style={{
                                                borderBottom: '1px solid rgba(255,255,255,0.04)',
                                                background: isCurrent ? 'rgba(124,77,255,0.08)' : undefined,
                                            }}>
                                                <td style={{ padding: '6px 8px', textAlign: 'center' }}>
                                                    <IconButton size="small" onClick={() => isCurrent && isPlaying ? pause() : handlePlayTrack(t)}>
                                                        {isCurrent && isPlaying ? (
                                                            <Pause fontSize="small" sx={{ color: '#B388FF' }} />
                                                        ) : (
                                                            <PlayArrow fontSize="small" />
                                                        )}
                                                    </IconButton>
                                                </td>
                                                <td style={{ padding: '10px 16px', fontWeight: 500, fontSize: 14, color: isCurrent ? '#B388FF' : '#E8EAED' }}>{t.title}</td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, cursor: t.albumId ? 'pointer' : 'default' }}
                                                    onClick={() => t.albumId && navigate(`/albums/${t.albumId}`)}>
                                                    <span style={{ color: t.albumId ? '#82B1FF' : '#9AA0A6' }}>{t.albumTitle ?? '—'}</span>
                                                </td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6', textAlign: 'right' }}>{fmt(t.durationSeconds)}</td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </Box>
                    </Card>
                </Box>
            )}

            {!hasAlbums && !hasTracks && !albumsLoading && !tracksLoading && (
                <Typography color="text.secondary" textAlign="center" sx={{ py: 4 }}>
                    No albums or tracks found for this artist.
                </Typography>
            )}
        </Box>
    );
};

export default ArtistDetailPage;
