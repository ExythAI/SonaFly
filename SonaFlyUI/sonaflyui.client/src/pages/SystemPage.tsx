import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, CircularProgress, Button, Divider,
    Dialog, DialogTitle, DialogContent, DialogContentText, DialogActions, Alert
} from '@mui/material';
import { DeleteForever, Warning } from '@mui/icons-material';
import { systemApi } from '../api/client';
import { useAuth } from '../auth/AuthContext';

const SystemPage: React.FC = () => {
    const qc = useQueryClient();
    const { isAdmin } = useAuth();
    const { data: status, isLoading } = useQuery({ queryKey: ['system-status'], queryFn: () => systemApi.status().then(r => r.data) });
    const [confirmOpen, setConfirmOpen] = useState(false);
    const [purgeResult, setPurgeResult] = useState<string | null>(null);

    const purgeMut = useMutation({
        mutationFn: () => systemApi.purge(),
        onSuccess: (res) => {
            setPurgeResult(res.data.message);
            setConfirmOpen(false);
            qc.invalidateQueries();
        },
        onError: (err: any) => {
            setPurgeResult(`Error: ${err.response?.data?.detail || err.message}`);
            setConfirmOpen(false);
        }
    });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    const rows = [
        ['Version', status?.version],
        ['Total Tracks', status?.totalTracks],
        ['Total Albums', status?.totalAlbums],
        ['Total Artists', status?.totalArtists],
        ['Total Genres', status?.totalGenres],
        ['Total Playlists', status?.totalPlaylists],
        ['Library Roots', status?.libraryRootCount],
        ['Current Scan', status?.currentScanStatus ?? 'Idle'],
        ['Last Scan Completed', status?.lastScanCompletedUtc ? new Date(status.lastScanCompletedUtc).toLocaleString() : 'Never'],
    ];

    return (
        <Box>
            <Typography variant="h4" gutterBottom>System</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>Server diagnostics and status</Typography>

            <Card sx={{ mb: 3 }}>
                <CardContent>
                    {rows.map(([label, value]) => (
                        <Box key={label as string} sx={{ display: 'flex', justifyContent: 'space-between', py: 1.2, borderBottom: '1px solid rgba(255,255,255,0.04)' }}>
                            <Typography variant="body2" color="text.secondary">{label}</Typography>
                            <Typography variant="body2" fontWeight={500}>{value ?? '—'}</Typography>
                        </Box>
                    ))}
                </CardContent>
            </Card>

            {purgeResult && (
                <Alert severity={purgeResult.startsWith('Error') ? 'error' : 'success'} sx={{ mb: 3 }}
                    onClose={() => setPurgeResult(null)}>{purgeResult}</Alert>
            )}

            {isAdmin && (
                <Card sx={{ border: '1px solid rgba(255,82,82,0.2)' }}>
                    <CardContent>
                        <Typography variant="h6" gutterBottom sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                            <Warning color="error" /> Maintenance
                        </Typography>
                        <Divider sx={{ mb: 2 }} />
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Box>
                                <Typography variant="subtitle2">Purge Library Data</Typography>
                                <Typography variant="body2" color="text.secondary">
                                    Delete all tracks, albums, artists, genres, playlists, artwork, and scan history.
                                    Library roots and user accounts are preserved.
                                </Typography>
                            </Box>
                            <Button variant="outlined" color="error" startIcon={<DeleteForever />}
                                onClick={() => setConfirmOpen(true)} disabled={purgeMut.isPending}>
                                {purgeMut.isPending ? 'Purging...' : 'Purge'}
                            </Button>
                        </Box>
                    </CardContent>
                </Card>
            )}

            <Dialog open={confirmOpen} onClose={() => setConfirmOpen(false)}>
                <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Warning color="error" /> Confirm Purge
                </DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        This will permanently delete <strong>all</strong> indexed music data, playlists, and cached artwork.
                        Library root configurations and user accounts will be kept. You'll need to re-scan
                        your libraries after purging. This action cannot be undone.
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirmOpen(false)}>Cancel</Button>
                    <Button variant="contained" color="error" onClick={() => purgeMut.mutate()}
                        disabled={purgeMut.isPending}>
                        {purgeMut.isPending ? <CircularProgress size={20} /> : 'Purge All Data'}
                    </Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default SystemPage;
