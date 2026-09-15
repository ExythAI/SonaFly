import { Alert } from '@mui/material';
import { useConfirm, errorMessage } from '../components/Feedback';
import { QueryError, EmptyState } from '../components/PageParts';
import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, Button, TextField, Dialog, DialogTitle,
    DialogContent, DialogActions, IconButton, Chip, CircularProgress, Grid, List, ListItem, ListItemText
} from '@mui/material';
import { Add, Delete } from '@mui/icons-material';
import { playlistsApi } from '../api/client';

const PlaylistsPage: React.FC = () => {
    const qc = useQueryClient();
    const confirm = useConfirm();
    const { data: playlists, isLoading, isError, error, refetch } = useQuery({ queryKey: ['playlists'], queryFn: () => playlistsApi.getAll().then(r => r.data) });
    const [open, setOpen] = useState(false);
    const [detail, setDetail] = useState<string | null>(null);
    const [form, setForm] = useState({ name: '', description: '' });

    const { data: playlist, isLoading: detailLoading, error: detailError, refetch: refetchDetail } = useQuery({
        queryKey: ['playlist', detail], queryFn: () => playlistsApi.getById(detail!).then(r => r.data), enabled: !!detail
    });

    const createMut = useMutation({ mutationFn: () => playlistsApi.create(form), onSuccess: () => { qc.invalidateQueries({ queryKey: ['playlists'] }); setOpen(false); setForm({ name: '', description: '' }); } });
    const deleteMut = useMutation({ mutationFn: (id: string) => playlistsApi.delete(id), onSuccess: () => { qc.invalidateQueries({ queryKey: ['playlists'] }); setDetail(null); } });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    const fmt = (s?: number | null) => { if (!s) return '--'; const m = Math.floor(s / 60); return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`; };

    if (isError) return <QueryError error={error} retry={refetch} />;

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2, mb: 3 }}>
                <Box><Typography variant="h4">Playlists</Typography><Typography variant="body2" color="text.secondary">Manage your playlists</Typography></Box>
                <Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}>Create Playlist</Button>
            </Box>

            <>{playlists?.length === 0 && <EmptyState title="Make room for your favorites" description="Create a playlist, then add tracks from your library." />}<Grid container spacing={2}>
                {playlists?.map((p) => (
                    <Grid key={p.id} size={{ xs: 12, sm: 6, md: 4 }}>
                        <Card tabIndex={0} onKeyDown={e => { if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); setDetail(p.id); } }} sx={{ cursor: 'pointer' }} onClick={() => setDetail(p.id)}>
                            <CardContent>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                    <Box><Typography variant="h6">{p.name}</Typography>{p.description && <Typography variant="body2" color="text.secondary">{p.description}</Typography>}</Box>
                                    <IconButton size="small" aria-label={`Delete ${p.name}`} disabled={deleteMut.isPending} onClick={async e => { e.stopPropagation(); if (await confirm({ title: `Delete “${p.name}”?`, description: "This permanently removes the playlist. The music in your library stays available." })) deleteMut.mutate(p.id); }} color="error"><Delete fontSize="small" /></IconButton>
                                </Box>
                                <Box sx={{ mt: 1, display: 'flex', gap: 1 }}>
                                    <Chip size="small" label={`${p.trackCount} tracks`} sx={{ height: 20 }} />
                                    {p.isPublic && <Chip size="small" label="Public" color="primary" sx={{ height: 20 }} />}
                                </Box>
                            </CardContent>
                        </Card>
                    </Grid>
                ))}
            </Grid></>

            {/* Create Dialog */}
            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>Create Playlist</DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
                    {createMut.isError && <Alert severity="error">{errorMessage(createMut.error)}</Alert>}
                    <TextField required label="Name" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
                    <TextField label="Description" value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} multiline rows={2} />
                </DialogContent>
                <DialogActions><Button onClick={() => setOpen(false)}>Cancel</Button><Button variant="contained" disabled={createMut.isPending || !form.name.trim()} onClick={() => createMut.mutate()}>{createMut.isPending ? 'Creating…' : 'Create'}</Button></DialogActions>
            </Dialog>

            {/* Detail Dialog */}
            <Dialog open={!!detail} onClose={() => setDetail(null)} maxWidth="sm" fullWidth>
                <DialogTitle>{playlist?.name}</DialogTitle>
                <DialogContent>
                    {detailLoading ? <CircularProgress aria-label="Loading playlist" /> : detailError ? <QueryError error={detailError} retry={refetchDetail} /> : (playlist?.items.length ?? 0) > 0 ? (
                        <List dense>
                            {playlist!.items.map((item, i) => (
                                <ListItem key={item.id} secondaryAction={<Typography variant="caption" color="text.secondary">{fmt(item.durationSeconds)}</Typography>}>
                                    <ListItemText primary={`${i + 1}. ${item.trackTitle}`} secondary={`${item.artistName ?? '—'} · ${item.albumTitle ?? '—'}`} />
                                </ListItem>
                            ))}
                        </List>
                    ) : <Typography color="text.secondary" textAlign="center" sx={{ py: 3 }}>No tracks in playlist</Typography>}
                </DialogContent>
                <DialogActions><Button onClick={() => setDetail(null)}>Close</Button></DialogActions>
            </Dialog>
        </Box>
    );
};

export default PlaylistsPage;
