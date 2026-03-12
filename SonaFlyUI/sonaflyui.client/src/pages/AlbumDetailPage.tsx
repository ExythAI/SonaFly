import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Box, Typography, Card, CardContent, IconButton, CircularProgress, Chip } from '@mui/material';
import { ArrowBack, PlayArrow, Pause } from '@mui/icons-material';
import { browseApi, artworkUrl } from '../api/client';
import { usePlayer } from '../components/PlayerContext';

const AlbumDetailPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const { play, pause, currentTrack, isPlaying } = usePlayer();
    const { data: album, isLoading } = useQuery({
        queryKey: ['album', id],
        queryFn: () => browseApi.albumById(id!).then(r => r.data),
        enabled: !!id,
    });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;
    if (!album) return <Typography>Album not found</Typography>;

    const fmt = (s?: number) => {
        if (!s) return '--';
        const m = Math.floor(s / 60);
        return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
    };

    const totalDuration = album.tracks?.reduce((sum: number, t: any) => sum + (t.durationSeconds || 0), 0) || 0;

    const trackQueue = album.tracks?.map((t: any) => ({
        id: t.id,
        title: t.title,
        artistName: t.artistName,
        albumTitle: album.title,
        artworkId: album.artworkId,
        durationSeconds: t.durationSeconds,
    })) ?? [];

    const handlePlay = (t: any) => {
        const track = {
            id: t.id,
            title: t.title,
            artistName: t.artistName,
            albumTitle: album.title,
            artworkId: album.artworkId,
            durationSeconds: t.durationSeconds,
        };
        play(track, trackQueue);
    };

    return (
        <Box>
            <IconButton onClick={() => navigate(-1)} sx={{ mb: 2 }}><ArrowBack /></IconButton>

            <Box sx={{ display: 'flex', gap: 3, mb: 4, flexDirection: { xs: 'column', sm: 'row' } }}>
                {/* Album Cover */}
                <Box sx={{
                    width: { xs: '100%', sm: 240 }, height: { xs: 240, sm: 240 },
                    borderRadius: 3, overflow: 'hidden', flexShrink: 0,
                    bgcolor: 'rgba(124,77,255,0.1)', position: 'relative'
                }}>
                    {album.artworkId ? (
                        <Box component="img" src={artworkUrl(album.artworkId)} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                    ) : (
                        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
                            <Typography variant="h1" sx={{ color: 'rgba(255,255,255,0.08)' }}>♪</Typography>
                        </Box>
                    )}
                </Box>

                {/* Album Info */}
                <Box sx={{ display: 'flex', flexDirection: 'column', justifyContent: 'flex-end' }}>
                    <Typography variant="caption" color="text.secondary" textTransform="uppercase" letterSpacing={1}>Album</Typography>
                    <Typography variant="h4" fontWeight={700} sx={{ mb: 0.5 }}>{album.title}</Typography>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                        {album.artistName && album.albumArtistId && (
                            <Typography variant="body1" fontWeight={500}
                                sx={{ color: 'primary.light', cursor: 'pointer', '&:hover': { textDecoration: 'underline' } }}
                                onClick={() => navigate(`/artists/${album.albumArtistId}`)}>
                                {album.artistName}
                            </Typography>
                        )}
                        {album.artistName && !album.albumArtistId && (
                            <Typography variant="body1" fontWeight={500}>{album.artistName}</Typography>
                        )}
                        {album.year && <Chip label={album.year} size="small" sx={{ height: 22 }} />}
                        {album.genreSummary && <Chip label={album.genreSummary} size="small" variant="outlined" sx={{ height: 22 }} />}
                        <Typography variant="body2" color="text.secondary">
                            {album.tracks?.length ?? 0} tracks · {fmt(totalDuration)}
                        </Typography>
                    </Box>
                </Box>
            </Box>

            {/* Track List */}
            <Card>
                <Box sx={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                        <thead>
                            <tr style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}>
                                <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px', width: 50 }}>#</th>
                                <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Title</th>
                                <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Artist</th>
                                <th style={{ textAlign: 'right', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px', width: 80 }}>Duration</th>
                                <th style={{ width: 50, padding: '12px 16px' }}></th>
                            </tr>
                        </thead>
                        <tbody>
                            {album.tracks?.map((t: any) => {
                                const isCurrent = currentTrack?.id === t.id;
                                return (
                                    <tr key={t.id} style={{
                                        borderBottom: '1px solid rgba(255,255,255,0.04)',
                                        background: isCurrent ? 'rgba(124,77,255,0.08)' : undefined,
                                    }}>
                                        <td style={{ padding: '10px 16px', color: isCurrent ? '#B388FF' : '#9AA0A6', fontSize: 13 }}>
                                            {t.discNumber && (album.discCount ?? 1) > 1 ? `${t.discNumber}-` : ''}{t.trackNumber ?? '—'}
                                        </td>
                                        <td style={{ padding: '10px 16px', fontWeight: 500, fontSize: 14, color: isCurrent ? '#B388FF' : '#E8EAED' }}>{t.title}</td>
                                        <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>{t.artistName ?? '—'}</td>
                                        <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6', textAlign: 'right' }}>{fmt(t.durationSeconds)}</td>
                                        <td style={{ padding: '10px 16px' }}>
                                            {isCurrent && isPlaying ? (
                                                <IconButton size="small" onClick={pause}>
                                                    <Pause fontSize="small" sx={{ color: '#B388FF' }} />
                                                </IconButton>
                                            ) : (
                                                <IconButton size="small" onClick={() => handlePlay(t)}>
                                                    <PlayArrow fontSize="small" />
                                                </IconButton>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </Box>
            </Card>
        </Box>
    );
};

export default AlbumDetailPage;
