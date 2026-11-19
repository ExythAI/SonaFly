import React, { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useSearchFilter } from '../hooks/useSearchFilter';
import { QueryError, EmptyState } from '../components/PageParts';
import { useQuery } from '@tanstack/react-query';
import { Box, Typography, TextField, InputAdornment, Card, CardActionArea, Grid, Chip, CircularProgress, Stack } from '@mui/material';
import { Search as SearchIcon } from '@mui/icons-material';
import { browseApi } from '../api/client';
import { Cover, TrackList, toTrack } from '../components/Music';

const SearchPage: React.FC = () => {
    const { input, setInput, value: query } = useSearchFilter();
    const { data, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['search', query],
        queryFn: () => browseApi.search(query, 20).then(r => r.data),
        enabled: query.length >= 2,
    });
    const tracks = useMemo(() => (data?.tracks ?? []).map(toTrack), [data]);
    const nothing = data && !data.artists?.length && !data.albums?.length && !data.tracks?.length;

    return (
        <Box>
            <Typography variant="h4" component="h1" gutterBottom>Search</Typography>
            <TextField fullWidth autoFocus placeholder="Search artists, albums, tracks..."
                label="Search music" value={input} onChange={e => setInput(e.target.value)}
                slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> } }}
                sx={{ mb: 3, '& .MuiOutlinedInput-root': { borderRadius: 3, bgcolor: 'background.paper' } }} />

            {isError && <QueryError error={error} retry={refetch} />}
            {query.length < 2 && <EmptyState title="Find your next listen" description="Enter at least two characters to search artists, albums, and tracks." />}
            {isLoading && <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}><CircularProgress /></Box>}

            {!isError && data && query.length >= 2 && (
                <Stack spacing={4}>
                    {data.artists?.length > 0 && (
                        <Box component="section">
                            <Typography variant="h6" component="h2" gutterBottom>Artists <Chip size="small" label={data.artists.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            <Grid container spacing={1.5}>
                                {data.artists.map(a => (
                                    <Grid key={a.id} size={{ xs: 12, sm: 6, md: 4 }}>
                                        <Card><CardActionArea component={Link} to={`/artists/${a.id}`} sx={{ p: 1.5 }}>
                                            <Stack direction="row" spacing={1.5} alignItems="center">
                                                <Cover artworkId={a.artworkId} kind="artist" round size={160} sx={{ width: 48 }} />
                                                <Box sx={{ minWidth: 0 }}>
                                                    <Typography variant="subtitle2" noWrap>{a.name}</Typography>
                                                    <Typography variant="caption" color="text.secondary">
                                                        {a.albumCount > 0 ? `${a.albumCount} ${a.albumCount === 1 ? 'album' : 'albums'}` : `${a.trackCount} ${a.trackCount === 1 ? 'track' : 'tracks'}`}
                                                    </Typography>
                                                </Box>
                                            </Stack>
                                        </CardActionArea></Card>
                                    </Grid>
                                ))}
                            </Grid>
                        </Box>
                    )}
                    {data.albums?.length > 0 && (
                        <Box component="section">
                            <Typography variant="h6" component="h2" gutterBottom>Albums <Chip size="small" label={data.albums.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            <Grid container spacing={1.5}>
                                {data.albums.map(a => (
                                    <Grid key={a.id} size={{ xs: 12, sm: 6, md: 4 }}>
                                        <Card><CardActionArea component={Link} to={`/albums/${a.id}`} sx={{ p: 1.5 }}>
                                            <Stack direction="row" spacing={1.5} alignItems="center">
                                                <Cover artworkId={a.artworkId} size={160} sx={{ width: 48, borderRadius: 1 }} />
                                                <Box sx={{ minWidth: 0 }}>
                                                    <Typography variant="subtitle2" noWrap>{a.title}</Typography>
                                                    <Typography variant="caption" color="text.secondary" noWrap component="div">
                                                        {[a.artistName, a.year].filter(Boolean).join(' · ')}
                                                    </Typography>
                                                </Box>
                                            </Stack>
                                        </CardActionArea></Card>
                                    </Grid>
                                ))}
                            </Grid>
                        </Box>
                    )}
                    {tracks.length > 0 && (
                        <Box component="section">
                            <Typography variant="h6" component="h2" gutterBottom>Tracks <Chip size="small" label={tracks.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            <Card><TrackList tracks={tracks} showCover /></Card>
                        </Box>
                    )}
                    {nothing && <Typography color="text.secondary" textAlign="center" sx={{ py: 4 }}>No results found for “{query}”</Typography>}
                </Stack>
            )}
        </Box>
    );
};

export default SearchPage;
