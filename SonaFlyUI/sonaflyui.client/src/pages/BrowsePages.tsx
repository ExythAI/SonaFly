import React from 'react';
import { useQuery } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, Grid, CircularProgress, CardActionArea
} from '@mui/material';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { browseApi, artworkUrl } from '../api/client';

const ArtistsPage: React.FC = () => {
    const navigate = useNavigate();
    const { data, isLoading } = useQuery({ queryKey: ['artists'], queryFn: () => browseApi.artists(1, 100).then(r => r.data) });
    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    return (
        <Box>
            <Typography variant="h4" gutterBottom>Artists</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>{data?.totalCount ?? 0} artists in library</Typography>
            <Grid container spacing={2}>
                {data?.items?.map((a: any) => (
                    <Grid key={a.id} size={{ xs: 6, sm: 4, md: 3, lg: 2 }}>
                        <Card><CardActionArea onClick={() => navigate(`/albums?artistId=${a.id}`)}><CardContent sx={{ textAlign: 'center', py: 3 }}>
                            <Box sx={{ width: 64, height: 64, borderRadius: '50%', mx: 'auto', mb: 1.5, bgcolor: 'rgba(124,77,255,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <Typography variant="h5" sx={{ color: 'primary.light' }}>{a.name[0]}</Typography>
                            </Box>
                            <Typography variant="subtitle2" noWrap>{a.name}</Typography>
                            <Typography variant="caption" color="text.secondary">{a.albumCount} albums · {a.trackCount} tracks</Typography>
                        </CardContent></CardActionArea></Card>
                    </Grid>
                ))}
            </Grid>
        </Box>
    );
};

const AlbumsPage: React.FC = () => {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const artistId = searchParams.get('artistId') ?? undefined;
    const { data, isLoading } = useQuery({
        queryKey: ['albums', artistId],
        queryFn: () => browseApi.albums(1, 100, artistId).then(r => r.data)
    });
    const { data: artistData } = useQuery({
        queryKey: ['artist', artistId],
        queryFn: () => browseApi.artistById(artistId!).then(r => r.data),
        enabled: !!artistId,
    });
    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    return (
        <Box>
            <Typography variant="h4" gutterBottom>
                {artistData ? `${artistData.name} — Albums` : 'Albums'}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                {data?.totalCount ?? 0} albums{artistData ? '' : ' in library'}
                {artistId && (
                    <Typography component="span" variant="body2" sx={{ ml: 1, color: 'primary.light', cursor: 'pointer', '&:hover': { textDecoration: 'underline' } }} onClick={() => navigate('/albums')}>
                        Show all albums
                    </Typography>
                )}
            </Typography>
            <Grid container spacing={2}>
                {data?.items?.map((a: any) => (
                    <Grid key={a.id} size={{ xs: 6, sm: 4, md: 3, lg: 2 }}>
                        <Card><CardActionArea onClick={() => navigate(`/albums/${a.id}`)}>
                            <Box sx={{ pt: '100%', position: 'relative', bgcolor: 'rgba(0,229,255,0.08)' }}>
                                {a.artworkId && <Box component="img" src={artworkUrl(a.artworkId)} sx={{ position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', objectFit: 'cover' }} />}
                                {!a.artworkId && <Box sx={{ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%,-50%)' }}><Typography variant="h4" sx={{ color: 'rgba(255,255,255,0.1)' }}>♪</Typography></Box>}
                            </Box>
                            <CardContent sx={{ py: 1.5 }}>
                                <Typography variant="subtitle2" noWrap>{a.title}</Typography>
                                <Typography variant="caption" color="text.secondary" noWrap>{a.artistName ?? 'Various'}{a.year ? ` · ${a.year}` : ''}</Typography>
                            </CardContent>
                        </CardActionArea></Card>
                    </Grid>
                ))}
            </Grid>
        </Box>
    );
};

const TracksPage: React.FC = () => {
    const { data, isLoading } = useQuery({ queryKey: ['tracks'], queryFn: () => browseApi.tracks(1, 100).then(r => r.data) });
    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    const fmt = (s?: number) => { if (!s) return '--'; const m = Math.floor(s / 60); return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`; };

    return (
        <Box>
            <Typography variant="h4" gutterBottom>Tracks</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>{data?.totalCount ?? 0} tracks</Typography>
            <Card>
                <Box sx={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                        <thead><tr style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}>
                            <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>#</th>
                            <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Title</th>
                            <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Artist</th>
                            <th style={{ textAlign: 'left', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Album</th>
                            <th style={{ textAlign: 'right', color: '#9AA0A6', fontSize: 12, fontWeight: 600, padding: '12px 16px' }}>Duration</th>
                        </tr></thead>
                        <tbody>
                            {data?.items?.map((t: any, i: number) => (
                                <tr key={t.id} style={{ borderBottom: '1px solid rgba(255,255,255,0.04)' }}>
                                    <td style={{ padding: '10px 16px', color: '#9AA0A6', fontSize: 13 }}>{t.trackNumber ?? i + 1}</td>
                                    <td style={{ padding: '10px 16px', fontWeight: 500, fontSize: 14, color: '#E8EAED' }}>{t.title}</td>
                                    <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>{t.artistName ?? '—'}</td>
                                    <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>{t.albumTitle ?? '—'}</td>
                                    <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6', textAlign: 'right' }}>{fmt(t.durationSeconds)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </Box>
            </Card>
        </Box>
    );
};

export { ArtistsPage, AlbumsPage, TracksPage };
