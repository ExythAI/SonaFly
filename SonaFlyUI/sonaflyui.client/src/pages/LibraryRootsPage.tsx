import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, Button, TextField, Dialog, DialogTitle,
    DialogContent, DialogActions, Table, TableHead, TableRow, TableCell, TableBody,
    IconButton, Chip, CircularProgress
} from '@mui/material';
import { Add, Delete, Edit, PlayArrow } from '@mui/icons-material';
import { libraryRootsApi } from '../api/client';

const LibraryRootsPage: React.FC = () => {
    const qc = useQueryClient();
    const { data: roots, isLoading } = useQuery({ queryKey: ['library-roots'], queryFn: () => libraryRootsApi.getAll().then(r => r.data) });
    const [open, setOpen] = useState(false);
    const [form, setForm] = useState({ name: '', path: '' });

    const createMut = useMutation({
        mutationFn: () => libraryRootsApi.create(form),
        onSuccess: () => { qc.invalidateQueries({ queryKey: ['library-roots'] }); setOpen(false); setForm({ name: '', path: '' }); }
    });
    const deleteMut = useMutation({
        mutationFn: (id: string) => libraryRootsApi.delete(id),
        onSuccess: () => qc.invalidateQueries({ queryKey: ['library-roots'] })
    });
    const scanMut = useMutation({
        mutationFn: (id: string) => libraryRootsApi.triggerScan(id),
        onSuccess: () => qc.invalidateQueries({ queryKey: ['library-roots'] })
    });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
                <Box>
                    <Typography variant="h4">Library Roots</Typography>
                    <Typography variant="body2" color="text.secondary">Manage your music library mount points</Typography>
                </Box>
                <Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}>Add Root</Button>
            </Box>

            <Card>
                <Table>
                    <TableHead><TableRow>
                        <TableCell>Name</TableCell><TableCell>Path</TableCell><TableCell>Status</TableCell>
                        <TableCell>Last Scan</TableCell><TableCell align="right">Actions</TableCell>
                    </TableRow></TableHead>
                    <TableBody>
                        {roots?.map((root: any) => (
                            <TableRow key={root.id}>
                                <TableCell><Typography fontWeight={600}>{root.name}</Typography></TableCell>
                                <TableCell><Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: 13, color: 'text.secondary' }}>{root.path}</Typography></TableCell>
                                <TableCell><Chip size="small" label={root.isEnabled ? 'Enabled' : 'Disabled'} color={root.isEnabled ? 'success' : 'default'} sx={{ height: 22 }} /></TableCell>
                                <TableCell>
                                    {root.lastScanStatus && <Chip size="small" label={root.lastScanStatus} sx={{ height: 22 }}
                                        color={root.lastScanStatus === 'Completed' ? 'success' : root.lastScanStatus === 'Running' ? 'warning' : 'error'} />}
                                </TableCell>
                                <TableCell align="right">
                                    <IconButton size="small" title="Scan" onClick={() => scanMut.mutate(root.id)}><PlayArrow fontSize="small" /></IconButton>
                                    <IconButton size="small" title="Delete" onClick={() => deleteMut.mutate(root.id)} color="error"><Delete fontSize="small" /></IconButton>
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </Card>

            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>Add Library Root</DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
                    <TextField label="Name" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} />
                    <TextField label="Path" value={form.path} onChange={e => setForm({ ...form, path: e.target.value })} placeholder="/music/library-main" helperText="Container-visible mount path" />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpen(false)}>Cancel</Button>
                    <Button variant="contained" onClick={() => createMut.mutate()}>Create</Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default LibraryRootsPage;
