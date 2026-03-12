import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, Grid, CircularProgress, CardActionArea, Button,
    TextField, InputAdornment, IconButton, Pagination
} from '@mui/material';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Search as SearchIcon, PlayArrow, Pause, ArrowUpward, ArrowDownward } from '@mui/icons-material';
import { browseApi, artworkUrl } from '../api/client';
import { usePlayer } from '../components/PlayerContext';

const PAGE_SIZE = 50;

const ArtistsPage: React.FC = () => {
    const navigate = useNavigate();
    const [pageSize, setPageSize] = useState(PAGE_SIZE);
    const { data, isLoading } = useQuery({
        queryKey: ['artists', pageSize],
        queryFn: () => browseApi.artists(1, pageSize).then(r => r.data)
    });
    const hasMore = (data?.items?.length ?? 0) < (data?.totalCount ?? 0);

    return (
        <Box>
            <Typography variant="h4" gutterBottom>Artists</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>{data?.totalCount ?? 0} artists in library</Typography>
            {isLoading ? <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box> : (
                <>
                    <Grid container spacing={2}>
                        {data?.items?.map((a: any) => (
                            <Grid key={a.id} size={{ xs: 6, sm: 4, md: 3, lg: 2 }}>
                                <Card><CardActionArea onClick={() => navigate(`/artists/${a.id}`)}><CardContent sx={{ textAlign: 'center', py: 3 }}>
                                    <Box sx={{ width: 64, height: 64, borderRadius: '50%', mx: 'auto', mb: 1.5, bgcolor: 'rgba(124,77,255,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                        <Typography variant="h5" sx={{ color: 'primary.light' }}>{a.name[0]}</Typography>
                                    </Box>
                                    <Typography variant="subtitle2" noWrap>{a.name}</Typography>
                                    <Typography variant="caption" color="text.secondary">{a.albumCount} albums · {a.trackCount} tracks</Typography>
                                </CardContent></CardActionArea></Card>
                            </Grid>
                        ))}
                    </Grid>
                    {hasMore && (
                        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
                            <Button variant="outlined" onClick={() => setPageSize(s => s + PAGE_SIZE)}>
                                Load More ({data?.items?.length} of {data?.totalCount})
                            </Button>
                        </Box>
                    )}
                </>
            )}
        </Box>
    );
};

const AlbumsPage: React.FC = () => {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const artistId = searchParams.get('artistId') ?? undefined;
    const [pageSize, setPageSize] = useState(PAGE_SIZE);
    const { data, isLoading } = useQuery({
        queryKey: ['albums', artistId, pageSize],
        queryFn: () => browseApi.albums(1, pageSize, artistId).then(r => r.data)
    });
    const { data: artistData } = useQuery({
        queryKey: ['artist', artistId],
        queryFn: () => browseApi.artistById(artistId!).then(r => r.data),
        enabled: !!artistId,
    });
    const hasMore = (data?.items?.length ?? 0) < (data?.totalCount ?? 0);

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
            {isLoading ? <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box> : (
                <>
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
                    {hasMore && (
                        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
                            <Button variant="outlined" onClick={() => setPageSize(s => s + PAGE_SIZE)}>
                                Load More ({data?.items?.length} of {data?.totalCount})
                            </Button>
                        </Box>
                    )}
                </>
            )}
        </Box>
    );
};

// ── Column definitions for sorting ──
const COLUMNS = [
    { key: 'title', label: 'Title', align: 'left' as const },
    { key: 'artist', label: 'Artist', align: 'left' as const },
    { key: 'album', label: 'Album', align: 'left' as const },
    { key: 'genre', label: 'Genre', align: 'left' as const },
    { key: 'duration', label: 'Duration', align: 'right' as const },
];

const TracksPage: React.FC = () => {
    const navigate = useNavigate();
    const { play, pause, currentTrack, isPlaying } = usePlayer();
    const [page, setPage] = useState(1);
    const [sortBy, setSortBy] = useState('title');
    const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
    const [filter, setFilter] = useState('');
    const [filterInput, setFilterInput] = useState('');

    const { data, isLoading } = useQuery({
        queryKey: ['tracks', page, sortBy, sortDir, filter],
        queryFn: () => browseApi.tracks(page, PAGE_SIZE, sortBy, sortDir, filter).then(r => r.data),
    });

    const totalPages = Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE);

    const handleSort = (col: string) => {
        if (sortBy === col) {
            setSortDir(d => d === 'asc' ? 'desc' : 'asc');
        } else {
            setSortBy(col);
            setSortDir('asc');
        }
        setPage(1);
    };

    const handleFilter = () => {
        setFilter(filterInput);
        setPage(1);
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') handleFilter();
    };

    const fmt = (s?: number) => {
        if (!s) return '--';
        const m = Math.floor(s / 60);
        return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
    };

    const handlePlayTrack = (t: any) => {
        const allTracks = data?.items?.map((tr: any) => ({
            id: tr.id, title: tr.title, artistName: tr.artistName,
            albumTitle: tr.albumTitle, artworkId: tr.artworkId, durationSeconds: tr.durationSeconds,
        })) ?? [];
        play({
            id: t.id, title: t.title, artistName: t.artistName,
            albumTitle: t.albumTitle, artworkId: t.artworkId, durationSeconds: t.durationSeconds,
        }, allTracks);
    };

    const thStyle = (col: string): React.CSSProperties => ({
        textAlign: col === 'duration' ? 'right' : 'left',
        color: sortBy === col ? '#B388FF' : '#9AA0A6',
        fontSize: 12, fontWeight: 600, padding: '12px 16px',
        cursor: 'pointer', userSelect: 'none', whiteSpace: 'nowrap',
    });

    const SortIcon = ({ col }: { col: string }) => {
        if (sortBy !== col) return null;
        return sortDir === 'asc'
            ? <ArrowUpward sx={{ fontSize: 14, ml: 0.5, verticalAlign: 'middle' }} />
            : <ArrowDownward sx={{ fontSize: 14, ml: 0.5, verticalAlign: 'middle' }} />;
    };

    return (
        <Box>
            <Typography variant="h4" gutterBottom>Tracks</Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
                <Typography variant="body2" color="text.secondary">
                    {data?.totalCount ?? 0} tracks{filter ? ` matching "${filter}"` : ''}
                </Typography>
                <Box sx={{ flex: 1 }} />
                <TextField
                    size="small" placeholder="Filter tracks..."
                    value={filterInput}
                    onChange={e => setFilterInput(e.target.value)}
                    onKeyDown={handleKeyDown}
                    onBlur={handleFilter}
                    slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }}
                    sx={{ width: 280, '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: 'background.paper' } }}
                />
                {filter && (
                    <Button size="small" variant="text" onClick={() => { setFilter(''); setFilterInput(''); setPage(1); }}>
                        Clear
                    </Button>
                )}
            </Box>

            {isLoading ? <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box> : (
                <>
                    <Card>
                        <Box sx={{ overflowX: 'auto' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}>
                                        <th style={{ width: 50, padding: '12px 8px' }}></th>
                                        {COLUMNS.map(col => (
                                            <th key={col.key} style={thStyle(col.key)} onClick={() => handleSort(col.key)}>
                                                {col.label}<SortIcon col={col.key} />
                                            </th>
                                        ))}
                                    </tr>
                                </thead>
                                <tbody>
                                    {data?.items?.map((t: any) => {
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
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>{t.artistName ?? '—'}</td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, cursor: t.albumId ? 'pointer' : 'default' }}
                                                    onClick={() => t.albumId && navigate(`/albums/${t.albumId}`)}>
                                                    <span style={{ color: t.albumId ? '#82B1FF' : '#9AA0A6' }}>{t.albumTitle ?? '—'}</span>
                                                </td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>{t.genre ?? '—'}</td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6', textAlign: 'right' }}>{fmt(t.durationSeconds)}</td>
                                            </tr>
                                        );
                                    })}
                                    {(!data?.items?.length) && (
                                        <tr><td colSpan={6} style={{ padding: '32px 16px', textAlign: 'center', color: '#9AA0A6' }}>
                                            {filter ? `No tracks matching "${filter}"` : 'No tracks found'}
                                        </td></tr>
                                    )}
                                </tbody>
                            </table>
                        </Box>
                    </Card>

                    {totalPages > 1 && (
                        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
                            <Pagination
                                count={totalPages}
                                page={page}
                                onChange={(_, p) => setPage(p)}
                                color="primary"
                                shape="rounded"
                            />
                        </Box>
                    )}
                </>
            )}
        </Box>
    );
};

export { ArtistsPage, AlbumsPage, TracksPage };
