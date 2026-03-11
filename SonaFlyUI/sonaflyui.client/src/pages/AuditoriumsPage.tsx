import React, { useState } from 'react';
import {
    Box, Typography, Button, TextField, Dialog, DialogTitle, DialogContent,
    DialogActions, Table, TableBody, TableCell, TableContainer, TableHead,
    TableRow, Paper, IconButton, Chip, Tooltip
} from '@mui/material';
import { Add, Delete, MeetingRoom, People } from '@mui/icons-material';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { auditoriumsApi } from '../api/client';

const AuditoriumsPage: React.FC = () => {
    const queryClient = useQueryClient();
    const [open, setOpen] = useState(false);
    const [name, setName] = useState('');

    const { data: auditoriums, isLoading } = useQuery({
        queryKey: ['auditoriums'],
        queryFn: () => auditoriumsApi.getAll().then(r => r.data as any[]),
    });

    const createMutation = useMutation({
        mutationFn: (name: string) => auditoriumsApi.create(name),
        onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['auditoriums'] }); setOpen(false); setName(''); },
    });

    const deleteMutation = useMutation({
        mutationFn: (id: string) => auditoriumsApi.delete(id),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['auditoriums'] }),
    });

    return (
        <Box>
            <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
                <Typography variant="h4" fontWeight={700}>
                    <MeetingRoom sx={{ mr: 1, verticalAlign: 'bottom' }} /> Auditoriums
                </Typography>
                <Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}
                    sx={{ background: 'linear-gradient(135deg, #4ECDC4, #44B3AA)', fontWeight: 600 }}>
                    Create Auditorium
                </Button>
            </Box>

            <TableContainer component={Paper} sx={{ bgcolor: 'background.paper', borderRadius: 3 }}>
                <Table>
                    <TableHead>
                        <TableRow>
                            <TableCell sx={{ fontWeight: 700 }}>Name</TableCell>
                            <TableCell sx={{ fontWeight: 700 }}>Listeners</TableCell>
                            <TableCell sx={{ fontWeight: 700 }}>Now Playing</TableCell>
                            <TableCell align="right" sx={{ fontWeight: 700 }}>Actions</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {isLoading ? (
                            <TableRow><TableCell colSpan={4} align="center">Loading…</TableCell></TableRow>
                        ) : auditoriums?.length === 0 ? (
                            <TableRow><TableCell colSpan={4} align="center" sx={{ color: 'text.secondary', py: 6 }}>
                                No auditoriums yet. Create one to get started!
                            </TableCell></TableRow>
                        ) : auditoriums?.map((a: any) => (
                            <TableRow key={a.id} hover>
                                <TableCell>
                                    <Box display="flex" alignItems="center" gap={1}>
                                        <MeetingRoom sx={{ color: '#4ECDC4' }} />
                                        <Typography fontWeight={600}>{a.name}</Typography>
                                    </Box>
                                </TableCell>
                                <TableCell>
                                    <Chip icon={<People />} label={`${a.activeUserCount} online`}
                                        size="small" variant="outlined"
                                        color={a.activeUserCount > 0 ? 'success' : 'default'} />
                                </TableCell>
                                <TableCell>
                                    {a.nowPlaying ? (
                                        <Chip label={`♪ ${a.nowPlaying}`} size="small"
                                            sx={{ bgcolor: 'rgba(78, 205, 196, 0.15)', color: '#4ECDC4' }} />
                                    ) : (
                                        <Typography variant="body2" color="text.secondary">Silent</Typography>
                                    )}
                                </TableCell>
                                <TableCell align="right">
                                    <Tooltip title="Delete auditorium">
                                        <IconButton color="error" onClick={() => {
                                            if (confirm(`Delete "${a.name}"?`))
                                                deleteMutation.mutate(a.id);
                                        }}>
                                            <Delete />
                                        </IconButton>
                                    </Tooltip>
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </TableContainer>

            {/* Create Dialog */}
            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xs" fullWidth>
                <DialogTitle>Create Auditorium</DialogTitle>
                <DialogContent>
                    <TextField autoFocus fullWidth label="Room Name" value={name}
                        onChange={e => setName(e.target.value)} margin="dense" variant="outlined"
                        placeholder="e.g. Main Room, Chill Zone" />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setOpen(false)}>Cancel</Button>
                    <Button variant="contained" disabled={!name.trim() || createMutation.isPending}
                        onClick={() => createMutation.mutate(name.trim())}
                        sx={{ background: 'linear-gradient(135deg, #4ECDC4, #44B3AA)' }}>
                        Create
                    </Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default AuditoriumsPage;
