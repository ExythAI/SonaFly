import React, { useState } from 'react';
import { useSearchFilter } from '../hooks/useSearchFilter';
import { EmptyState, QueryError } from '../components/PageParts';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import {
    Box, Typography, Card, Grid, CircularProgress, Button,
    TextField, InputAdornment, IconButton, Pagination, Stack, ToggleButton, ToggleButtonGroup,
} from '@mui/material';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Search as SearchIcon, PlayArrow, Pause, ArrowUpward, ArrowDownward } from '@mui/icons-material';
import { browseApi } from '../api/client';
import type { TrackListItemDto } from '../api/types.ts';
import { usePlayer } from '../components/PlayerContext';
import { AlbumGrid, ArtistCard } from '../components/Cards';
import { FavoriteButton, TrackMenu, toTrack } from '../components/Music';

const PAGE_SIZE = 60;

/** The filter box and sort choice above a grid. Both live in the URL, so Back restores them. */
function BrowseControls({ label, filter, onFilter, sort, sorts, onSort }: {
    label: string; filter: string; onFilter: (value: string) => void;
    sort: string; sorts: { value: string; label: string }[]; onSort: (value: string) => void;
}) {
    return <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }} sx={{ mb: 3 }}>
        <TextField size="small" label={label} placeholder="Type to filter…" value={filter} onChange={e => onFilter(e.target.value)}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }}
            sx={{ width: { xs: '100%', sm: 300 }, '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: 'background.paper' } }} />
        <ToggleButtonGroup size="small" exclusive value={sort} aria-label="Sort order"
            onChange={(_, value: string | null) => { if (value !== null) onSort(value); }}
            sx={{ '& .MuiToggleButton-root': { px: 1.75, textTransform: 'none' } }}>
            {sorts.map(s => <ToggleButton key={s.value} value={s.value}>{s.label}</ToggleButton>)}
        </ToggleButtonGroup>
    </Stack>;
}

/** Reads and writes one URL parameter, dropping it when it holds the default. */
function useUrlChoice(key: string, fallback: string): [string, (value: string) => void] {
    const [params, setParams] = useSearchParams();
    const value = params.get(key) ?? fallback;
    const set = (next: string) => setParams(previous => {
        const copy = new URLSearchParams(previous);
        if (next === fallback) copy.delete(key); else copy.set(key, next);
        return copy;
    }, { replace: true });
    return [value, set];
}

function LoadMore({ shown, total, onMore }: { shown: number; total: number; onMore: () => void }) {
    if (shown >= total) return null;
    return <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
        <Button variant="outlined" onClick={onMore}>Show more ({shown} of {total})</Button>
    </Box>;
}

const ARTIST_SORTS = [{ value: 'name', label: 'A–Z' }, { value: 'tracks', label: 'Most music' }];

const ArtistsPage: React.FC = () => {
    const [pageSize, setPageSize] = useState(PAGE_SIZE);
    const { input, setInput, value: filter } = useSearchFilter();
    const [sort, setSort] = useUrlChoice('sort', 'name');
    const { data, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['artists', pageSize, filter, sort],
        queryFn: () => browseApi.artists(1, pageSize, filter, sort).then(r => r.data),
        placeholderData: keepPreviousData,
    });
    const artists = data?.items ?? [];

    return (
        <Box>
            <Typography variant="h4" component="h1" gutterBottom>Artists</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {data?.totalCount ?? 0} {filter ? `matching “${filter}”` : 'artists in your library'}
            </Typography>
            <BrowseControls label="Find an artist" filter={input} onFilter={setInput} sort={sort} sorts={ARTIST_SORTS} onSort={setSort} />
            {isError ? <QueryError error={error} retry={refetch} /> : isLoading ? <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box> : (
                <>
                    {artists.length === 0 && <EmptyState title="No artists found" description={filter ? 'Nothing matches that. Try fewer letters.' : 'Scan a music folder to fill your library.'} />}
                    <Grid container spacing={2}>
                        {artists.map(a => <Grid key={a.id} size={{ xs: 6, sm: 4, md: 3, lg: 2 }}><ArtistCard artist={a} /></Grid>)}
                    </Grid>
                    <LoadMore shown={artists.length} total={data?.totalCount ?? 0} onMore={() => setPageSize(s => s + PAGE_SIZE)} />
                </>
            )}
        </Box>
    );
};

const ALBUM_SORTS = [{ value: 'year', label: 'By year' }, { value: 'recent', label: 'Recently added' }, { value: 'title', label: 'A–Z' }];

const AlbumsPage: React.FC = () => {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const artistId = searchParams.get('artistId') ?? undefined;
    const [pageSize, setPageSize] = useState(PAGE_SIZE);
    const { input, setInput, value: filter } = useSearchFilter();
    const [sort, setSort] = useUrlChoice('sort', 'year');
    const { data, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['albums', artistId, pageSize, sort, filter],
        queryFn: () => browseApi.albums(1, pageSize, artistId, sort, filter).then(r => r.data),
        placeholderData: keepPreviousData,
    });
    const { data: artistData } = useQuery({
        queryKey: ['artist', artistId],
        queryFn: () => browseApi.artistById(artistId!).then(r => r.data),
        enabled: !!artistId,
    });
    const albums = data?.items ?? [];

    return (
        <Box>
            <Typography variant="h4" component="h1" gutterBottom>
                {artistData ? `${artistData.name} — Albums` : 'Albums'}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {data?.totalCount ?? 0} {filter ? `matching “${filter}”` : `albums${artistData ? '' : ' in your library'}`}
                {artistId && (
                    <Typography component="span" variant="body2" sx={{ ml: 1, color: 'primary.light', cursor: 'pointer', '&:hover': { textDecoration: 'underline' } }} onClick={() => navigate('/albums')}>
                        Show all albums
                    </Typography>
                )}
            </Typography>
            <BrowseControls label="Find an album or artist" filter={input} onFilter={setInput} sort={sort} sorts={ALBUM_SORTS} onSort={setSort} />
            {isError ? <QueryError error={error} retry={refetch} /> : isLoading ? <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box> : (
                <>
                    {albums.length === 0 && <EmptyState title="No albums found" description={filter ? 'Nothing matches that. Try fewer letters.' : 'Scan a music folder to fill your library.'} />}
                    <AlbumGrid albums={albums} />
                    <LoadMore shown={albums.length} total={data?.totalCount ?? 0} onMore={() => setPageSize(s => s + PAGE_SIZE)} />
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

const cellLink: React.CSSProperties = { color: '#82B1FF', textDecoration: 'none' };

const TracksPage: React.FC = () => {
    const { play, pause, resume, currentTrack, isPlaying, sharedRoom } = usePlayer();
    const [params, setParams] = useSearchParams();
    const page = Math.max(1, Number(params.get('page')) || 1);
    const sortBy = params.get('sort') ?? 'title';
    const sortDir = params.get('direction') === 'desc' ? 'desc' : 'asc';
    const { input: filterInput, setInput: setFilterInput, value: filter } = useSearchFilter();
    const setPage = (value: number) => setParams(previous => { const next = new URLSearchParams(previous); next.set('page', String(value)); return next; });

    const { data, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['tracks', page, sortBy, sortDir, filter],
        queryFn: () => browseApi.tracks(page, 50, sortBy, sortDir, filter).then(r => r.data),
    });

    const totalPages = Math.ceil((data?.totalCount ?? 0) / 50);

    const handleSort = (col: string) => setParams(previous => {
        const next = new URLSearchParams(previous);
        next.set('sort', col);
        next.set('direction', sortBy === col && sortDir === 'asc' ? 'desc' : 'asc');
        next.delete('page');
        return next;
    });

    const fmt = (s?: number | null) => {
        if (!s) return '--';
        const m = Math.floor(s / 60);
        return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
    };

    const handlePlayTrack = (t: TrackListItemDto) => play(toTrack(t), data?.items?.map(toTrack) ?? []);

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
            <Typography variant="h4" component="h1" gutterBottom>Tracks</Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 2, mb: 3 }}>
                <Typography variant="body2" color="text.secondary">
                    {data?.totalCount ?? 0} tracks{filter ? ` matching "${filter}"` : ''}
                </Typography>
                <Box sx={{ flex: 1 }} />
                <TextField
                    size="small" label="Search tracks" placeholder="Filter tracks..."
                    value={filterInput}
                    onChange={e => setFilterInput(e.target.value)}
                    slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }}
                    sx={{ width: 280, '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: 'background.paper' } }}
                />
                {filter && (
                    <Button size="small" variant="text" onClick={() => { setFilterInput(''); }}>
                        Clear
                    </Button>
                )}
            </Box>

            {isError ? <QueryError error={error} retry={refetch} /> : isLoading ? <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box> : (
                <>
                    <Card>
                        <Box sx={{ display: { xs: 'block', sm: 'none' } }}>
                            {data?.items?.map((track) => {
                                const selected = !sharedRoom && currentTrack?.id === track.id;
                                return <Stack key={track.id} direction="row" alignItems="center" spacing={1} sx={{ p: 1.5, borderBottom: '1px solid', borderColor: 'divider', bgcolor: selected ? 'rgba(124,77,255,.1)' : undefined }}>
                                    <IconButton aria-label={selected && isPlaying ? `Pause ${track.title}` : `Play ${track.title}`} onClick={() => selected ? (isPlaying ? pause() : resume()) : handlePlayTrack(track)}>{selected && isPlaying ? <Pause /> : <PlayArrow />}</IconButton>
                                    <Box minWidth={0} flex={1}><Typography variant="body2" fontWeight={600} noWrap>{track.title}</Typography><Typography variant="caption" color="text.secondary" noWrap component="div">{track.artistName || 'Unknown artist'}</Typography></Box>
                                    <Typography variant="caption" color="text.secondary">{fmt(track.durationSeconds)}</Typography>
                                    <TrackMenu track={toTrack(track)} />
                                </Stack>;
                            })}
                            {!data?.items?.length && <Typography textAlign="center" color="text.secondary" p={3}>No matching tracks.</Typography>}
                        </Box>
                        <Box sx={{ overflowX: 'auto', display: { xs: 'none', sm: 'block' } }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '1px solid rgba(255,255,255,0.06)' }}>
                                        <th style={{ width: 50, padding: '12px 8px' }}></th>
                                        {COLUMNS.map(col => (
                                            <th key={col.key} style={thStyle(col.key)} aria-sort={sortBy === col.key ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'}>
                                                <Button size="small" color="inherit" onClick={() => handleSort(col.key)}>{col.label}<SortIcon col={col.key} /></Button>
                                            </th>
                                        ))}
                                        <th style={{ width: 84 }}><span style={{ position: 'absolute', left: -9999 }}>Actions</span></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {data?.items?.map((t) => {
                                        const isCurrent = !sharedRoom && currentTrack?.id === t.id;
                                        return (
                                            <tr key={t.id} style={{
                                                borderBottom: '1px solid rgba(255,255,255,0.04)',
                                                background: isCurrent ? 'rgba(124,77,255,0.08)' : undefined,
                                            }}>
                                                <td style={{ padding: '6px 8px', textAlign: 'center' }}>
                                                    <IconButton size="small" aria-label={isCurrent && isPlaying ? `Pause ${t.title}` : `Play ${t.title}`} onClick={() => isCurrent ? (isPlaying ? pause() : resume()) : handlePlayTrack(t)}>
                                                        {isCurrent && isPlaying ? (
                                                            <Pause fontSize="small" sx={{ color: '#B388FF' }} />
                                                        ) : (
                                                            <PlayArrow fontSize="small" />
                                                        )}
                                                    </IconButton>
                                                </td>
                                                <td style={{ padding: '10px 16px', fontWeight: 500, fontSize: 14, color: isCurrent ? '#B388FF' : '#E8EAED' }}>{t.title}</td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>
                                                    {t.artistId ? <Link to={`/artists/${t.artistId}`} style={cellLink}>{t.artistName ?? '—'}</Link> : t.artistName ?? '—'}
                                                </td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>
                                                    {t.albumId ? <Link to={`/albums/${t.albumId}`} style={cellLink}>{t.albumTitle ?? '—'}</Link> : t.albumTitle ?? '—'}
                                                </td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6' }}>{t.genre ?? '—'}</td>
                                                <td style={{ padding: '10px 16px', fontSize: 13, color: '#9AA0A6', textAlign: 'right' }}>{fmt(t.durationSeconds)}</td>
                                                <td style={{ padding: '2px 8px', whiteSpace: 'nowrap', textAlign: 'right' }}>
                                                    <FavoriteButton track={t} /><TrackMenu track={toTrack(t)} />
                                                </td>
                                            </tr>
                                        );
                                    })}
                                    {(!data?.items?.length) && (
                                        <tr><td colSpan={7} style={{ padding: '32px 16px', textAlign: 'center', color: '#9AA0A6' }}>
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
