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
    const { data: playlists, isLoading } = useQuery({ queryKey: ['playlists'], queryFn: () => playlistsApi.getAll().then(r => r.data) });
    const [open, setOpen] = useState(false);
    const [detail, setDetail] = useState<string | null>(null);
    const [form, setForm] = useState({ name: '', description: '' });

    const { data: playlist } = useQuery({
        queryKey: ['playlist', detail], queryFn: () => playlistsApi.getById(detail!).then(r => r.data), enabled: !!detail
    });

    const createMut = useMutation({ mutationFn: () => playlistsApi.create(form), onSuccess: () => { qc.invalidateQueries({ queryKey: ['playlists'] }); setOpen(false); setForm({ name: '', description: '' }); } });
    const deleteMut = useMutation({ mutationFn: (id: string) => playlistsApi.delete(id), onSuccess: () => { qc.invalidateQueries({ queryKey: ['playlists'] }); setDetail(null); } });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    const fmt = (s?: number) => { if (!s) return '--'; const m = Math.floor(s / 60); return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}`; };

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
                <Box><Typography variant="h4">Playlists</Typography><Typography variant="body2" color="text.secondary">Manage your playlists</Typography></Box>
                <Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}>Create Playlist</Button>
            </Box>

            <Grid container spacing={2}>
                {playlists?.map((p: any) => (
                    <Grid key={p.id} size={{ xs: 12, sm: 6, md: 4 }}>
                        <Card sx={{ cursor: 'pointer' }} onClick={() => setDetail(p.id)}>
                            <CardContent>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                    <Box><Typography variant="h6">{p.name}</Typography>{p.description && <Typography variant="body2" color="text.secondary">{p.description}</Typography>}</Box>
                                    <IconButton size="small" onClick={e => { e.stopPropagation(); deleteMut.mutate(p.id); }} color="error"><Delete fontSize="small" /></IconButton>
                                </Box>
                                <Box sx={{ mt: 1, display: 'flex', gap: 1 }}>
                                    <Chip size="small" label={`${p.trackCount} tracks`} sx={{ height: 20 }} />
                                    {p.isPublic && <Chip size="small" label="Public" color="primary" sx={{ height: 20 }} />}
                                </Box>
                            </CardContent>
                        </Card>
                    </Grid>
                ))}
            </Grid>

            {/* Create Dialog */}
            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>Create Playlist</DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
                    <TextField label="Name" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
                    <TextField label="Description" value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} multiline rows={2} />
                </DialogContent>
                <DialogActions><Button onClick={() => setOpen(false)}>Cancel</Button><Button variant="contained" onClick={() => createMut.mutate()}>Create</Button></DialogActions>
            </Dialog>

            {/* Detail Dialog */}
            <Dialog open={!!detail} onClose={() => setDetail(null)} maxWidth="sm" fullWidth>
                <DialogTitle>{playlist?.name}</DialogTitle>
                <DialogContent>
                    {playlist?.items?.length > 0 ? (
                        <List dense>
                            {playlist.items.map((item: any, i: number) => (
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
