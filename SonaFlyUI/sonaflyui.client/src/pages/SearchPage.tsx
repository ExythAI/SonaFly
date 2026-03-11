import React, { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Box, Typography, TextField, InputAdornment, Card, CardContent, Grid, Chip, CircularProgress } from '@mui/material';
import { Search as SearchIcon } from '@mui/icons-material';
import { browseApi } from '../api/client';

const SearchPage: React.FC = () => {
    const [query, setQuery] = useState('');
    const { data, isLoading } = useQuery({
        queryKey: ['search', query],
        queryFn: () => browseApi.search(query, 20).then(r => r.data),
        enabled: query.length >= 2,
    });

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
                                        <Card><CardContent sx={{ py: 1.5, display: 'flex', alignItems: 'center', gap: 1.5 }}>
                                            <Box sx={{ width: 40, height: 40, borderRadius: '50%', bgcolor: 'rgba(124,77,255,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                                <Typography sx={{ color: 'primary.light' }}>{a.name[0]}</Typography>
                                            </Box>
                                            <Box><Typography variant="subtitle2">{a.name}</Typography><Typography variant="caption" color="text.secondary">{a.albumCount} albums</Typography></Box>
                                        </CardContent></Card>
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
                                        <Card><CardContent sx={{ py: 1.5 }}><Typography variant="subtitle2">{a.title}</Typography><Typography variant="caption" color="text.secondary">{a.artistName}{a.year ? ` · ${a.year}` : ''}</Typography></CardContent></Card>
                                    </Grid>
                                ))}
                            </Grid>
                        </Grid>
                    )}
                    {data.tracks?.length > 0 && (
                        <Grid size={{ xs: 12 }}>
                            <Typography variant="h6" gutterBottom>Tracks <Chip size="small" label={data.tracks.length} sx={{ ml: 1, height: 20 }} /></Typography>
                            {data.tracks.map((t: any) => (
                                <Card key={t.id} sx={{ mb: 1 }}><CardContent sx={{ py: 1, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                    <Box><Typography variant="subtitle2">{t.title}</Typography><Typography variant="caption" color="text.secondary">{t.artistName} · {t.albumTitle}</Typography></Box>
                                </CardContent></Card>
                            ))}
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
