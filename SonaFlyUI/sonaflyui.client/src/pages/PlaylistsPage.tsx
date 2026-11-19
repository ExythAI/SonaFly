import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Alert, Box, Button, Card, CardActionArea, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
    Grid, Stack, TextField, Typography,
} from '@mui/material';
import { Add, QueueMusic } from '@mui/icons-material';
import { playlistsApi } from '../api/client';
import { errorMessage } from '../components/Feedback';
import { EmptyState, PageHeader, PageLoading, QueryError } from '../components/PageParts';

const PlaylistsPage: React.FC = () => {
    const qc = useQueryClient();
    const { data: playlists, isLoading, isError, error, refetch } = useQuery({ queryKey: ['playlists'], queryFn: () => playlistsApi.getAll().then(r => r.data) });
    const [open, setOpen] = useState(false);
    const [form, setForm] = useState({ name: '', description: '' });

    const createMut = useMutation({
        mutationFn: () => playlistsApi.create(form),
        onSuccess: () => { void qc.invalidateQueries({ queryKey: ['playlists'] }); setOpen(false); setForm({ name: '', description: '' }); },
    });

    if (isLoading) return <PageLoading />;
    if (isError) return <QueryError error={error} retry={refetch} />;

    return (
        <Box>
            <PageHeader title="Playlists" subtitle="Your own running orders. Add tracks from the ⋮ menu anywhere in the library."
                action={<Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}>New playlist</Button>} />

            {playlists?.length === 0 && <EmptyState title="Make room for your favourites"
                description="Create a playlist, then add tracks to it from any album, search result or track list."
                action={<Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}>New playlist</Button>} />}

            <Grid container spacing={2}>
                {playlists?.map(p => (
                    <Grid key={p.id} size={{ xs: 12, sm: 6, md: 4 }}>
                        <Card>
                            <CardActionArea component={Link} to={`/playlists/${p.id}`}>
                                <CardContent>
                                    <Stack direction="row" spacing={2} alignItems="center">
                                        <Box sx={{ width: 56, height: 56, borderRadius: 2, flexShrink: 0, display: 'grid', placeItems: 'center',
                                            background: 'linear-gradient(135deg, rgba(124,77,255,.45), rgba(0,229,255,.18))' }}>
                                            <QueueMusic sx={{ color: 'rgba(255,255,255,.8)' }} />
                                        </Box>
                                        <Box sx={{ minWidth: 0 }}>
                                            <Typography variant="h6" noWrap title={p.name}>{p.name}</Typography>
                                            {p.description && <Typography variant="body2" color="text.secondary" noWrap>{p.description}</Typography>}
                                            <Stack direction="row" spacing={1} sx={{ mt: 0.5 }}>
                                                <Chip size="small" label={`${p.trackCount} ${p.trackCount === 1 ? 'track' : 'tracks'}`} sx={{ height: 20 }} />
                                                {p.isPublic && <Chip size="small" label="Public" color="primary" sx={{ height: 20 }} />}
                                            </Stack>
                                        </Box>
                                    </Stack>
                                </CardContent>
                            </CardActionArea>
                        </Card>
                    </Grid>
                ))}
            </Grid>

            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>New playlist</DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
                    {createMut.isError && <Alert severity="error">{errorMessage(createMut.error)}</Alert>}
                    <TextField required autoFocus label="Name" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
                    <TextField label="Description" value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} multiline rows={2} />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpen(false)}>Cancel</Button>
                    <Button variant="contained" disabled={createMut.isPending || !form.name.trim()} onClick={() => createMut.mutate()}>{createMut.isPending ? 'Creating…' : 'Create'}</Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default PlaylistsPage;
