import { QueryError } from '../components/PageParts';
import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, CardContent, CircularProgress, Button, Divider,
    Dialog, DialogTitle, DialogContent, DialogContentText, DialogActions, Alert
} from '@mui/material';
import { Backup, DeleteForever, Warning } from '@mui/icons-material';
import { systemApi } from '../api/client';
import { errorMessage } from '../components/Feedback';
import { useAuth } from '../auth/AuthContext';

const SystemPage: React.FC = () => {
    const qc = useQueryClient();
    const { isAdmin } = useAuth();
    const { data: status, isLoading, isError, error, refetch } = useQuery({ queryKey: ['system-status'], queryFn: () => systemApi.status().then(r => r.data) });
    const [confirmOpen, setConfirmOpen] = useState(false);
    const [purgeResult, setPurgeResult] = useState<string | null>(null);

    const purgeMut = useMutation({
        mutationFn: () => systemApi.purge(),
        onSuccess: (res) => {
            setPurgeResult(res.data.message);
            setConfirmOpen(false);
            qc.invalidateQueries();
        },
        onError: (err) => {
            setPurgeResult(`Error: ${errorMessage(err)}`);
            setConfirmOpen(false);
        }
    });

    const backups = useQuery({
        queryKey: ['system-backups'],
        queryFn: () => systemApi.backups().then(r => r.data),
        enabled: isAdmin,
    });
    const backupMut = useMutation({
        mutationFn: () => systemApi.createBackup(),
        onSuccess: (res) => {
            setPurgeResult(`Backup written: ${res.data.fileName}`);
            qc.invalidateQueries({ queryKey: ['system-backups'] });
        },
        onError: (err) => setPurgeResult(`Error: ${errorMessage(err)}`),
    });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    if (isError) return <QueryError error={error} retry={refetch} />;

    // Totals are now reported as the caller sees them, and the deployment/scan rows are
    // server administration rather than library content, so only admins are shown them.
    const rows = [
        ['Version', status?.version],
        ['Total Tracks', status?.totalTracks],
        ['Total Albums', status?.totalAlbums],
        ['Total Artists', status?.totalArtists],
        ['Total Genres', status?.totalGenres],
        ['Total Playlists', status?.totalPlaylists],
        ...(isAdmin ? [
            ['Library Roots', status?.libraryRootCount],
            ['Current Scan', status?.currentScanStatus ?? 'Idle'],
            ['Last Scan Completed', status?.lastScanCompletedUtc ? new Date(status.lastScanCompletedUtc).toLocaleString() : 'Never'],
        ] : []),
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
                <Card sx={{ mb: 3 }}>
                    <CardContent>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2, mb: 1 }}>
                            <Box>
                                <Typography variant="h6">Database backups</Typography>
                                <Typography variant="body2" color="text.secondary">
                                    Taken every night, and before a purge or an upgrade that changes the database.
                                    {backups.data?.directory ? ` Kept in ${backups.data.directory}.` : ''}
                                </Typography>
                            </Box>
                            <Button variant="outlined" startIcon={<Backup />} onClick={() => backupMut.mutate()}
                                disabled={backupMut.isPending}>
                                {backupMut.isPending ? 'Backing up...' : 'Back up now'}
                            </Button>
                        </Box>
                        <Divider sx={{ mb: 1 }} />
                        {backups.isError && <Alert severity="error">{errorMessage(backups.error)}</Alert>}
                        {backups.data && backups.data.backups.length === 0 && (
                            <Typography variant="body2" color="text.secondary">No backups yet.</Typography>
                        )}
                        {backups.data?.backups.slice(0, 8).map(file => (
                            <Box key={file.fileName} sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, py: 0.8, borderBottom: '1px solid rgba(255,255,255,0.04)' }}>
                                <Typography variant="body2" noWrap title={file.fileName}>
                                    {new Date(file.createdUtc).toLocaleString()} · {file.reason}
                                </Typography>
                                <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
                                    {(file.sizeBytes / (1024 * 1024)).toFixed(1)} MB
                                </Typography>
                            </Box>
                        ))}
                    </CardContent>
                </Card>
            )}

            {isAdmin && (
                <Card sx={{ border: '1px solid rgba(255,82,82,0.2)' }}>
                    <CardContent>
                        <Typography variant="h6" gutterBottom sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                            <Warning color="error" /> Maintenance
                        </Typography>
                        <Divider sx={{ mb: 2 }} />
                        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2 }}>
                            <Box>
                                <Typography variant="subtitle2">Purge Library Data</Typography>
                                <Typography variant="body2" color="text.secondary">
                                    Delete all tracks, albums, artists, genres, artwork, and scan history. A backup is taken first.
                                    Library roots, user accounts, playlists and mixed tapes are kept; their tracks, favourites and restrictions come back when you rescan the same files.
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
                        This will permanently delete <strong>all</strong> indexed music data, play history, and cached artwork.
                        Library roots, user accounts, playlists and mixed tapes will be kept, and a database backup is taken first. You'll need to re-scan
                        your libraries after purging; playlist contents, favourites and restrictions return as the same files are found.
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
