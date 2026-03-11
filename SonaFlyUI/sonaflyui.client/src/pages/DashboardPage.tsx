import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { Box, Typography, Grid, Card, CardContent, Chip, CircularProgress } from '@mui/material';
import { Album, MusicNote, Person, QueueMusic, FolderOpen, Refresh } from '@mui/icons-material';
import { systemApi, scansApi } from '../api/client';

const StatCard: React.FC<{ label: string; value: number | string; icon: React.ReactNode; color: string }> = ({ label, value, icon, color }) => (
    <Card>
        <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 2.5 }}>
            <Box sx={{ p: 1.5, borderRadius: 2, bgcolor: `${color}15` }}>{icon}</Box>
            <Box>
                <Typography variant="h5" fontWeight={700}>{value}</Typography>
                <Typography variant="body2" color="text.secondary">{label}</Typography>
            </Box>
        </CardContent>
    </Card>
);

const DashboardPage: React.FC = () => {
    const { data: status, isLoading } = useQuery({ queryKey: ['system-status'], queryFn: () => systemApi.status().then(r => r.data) });
    const { data: scans } = useQuery({ queryKey: ['recent-scans'], queryFn: () => scansApi.getAll().then(r => r.data) });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    const isFirstRun = status?.totalTracks === 0 && status?.libraryRootCount === 0;

    return (
        <Box>
            <Typography variant="h4" gutterBottom>Dashboard</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>Overview of your music server</Typography>

            {isFirstRun && (
                <Card sx={{ mb: 3, border: '1px solid', borderColor: 'primary.main', bgcolor: 'rgba(124,77,255,0.08)' }}>
                    <CardContent>
                        <Typography variant="h6" gutterBottom sx={{ color: 'primary.main' }}>
                            🎉 Welcome to SonaFly!
                        </Typography>
                        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                            Your music server is ready. Follow these steps to get started:
                        </Typography>
                        <Box component="ol" sx={{ pl: 2, '& li': { mb: 1 } }}>
                            <li><Typography variant="body2">Go to <strong>Library Roots</strong> and add the path(s) to your music folders (e.g., <code>/music/library-main</code>)</Typography></li>
                            <li><Typography variant="body2">Click <strong>Scan</strong> on each library root to index your collection</Typography></li>
                            <li><Typography variant="body2">Go to <strong>Users</strong> to create accounts for your listeners</Typography></li>
                            <li><Typography variant="body2"><strong>Change the admin password</strong> — the default is <code>Admin123!</code></Typography></li>
                        </Box>
                    </CardContent>
                </Card>
            )}
            <Grid container spacing={2.5} sx={{ mb: 4 }}>
                <Grid size={{ xs: 6, md: 3 }}><StatCard label="Tracks" value={status?.totalTracks ?? 0} icon={<MusicNote sx={{ color: '#7C4DFF' }} />} color="#7C4DFF" /></Grid>
                <Grid size={{ xs: 6, md: 3 }}><StatCard label="Albums" value={status?.totalAlbums ?? 0} icon={<Album sx={{ color: '#00E5FF' }} />} color="#00E5FF" /></Grid>
                <Grid size={{ xs: 6, md: 3 }}><StatCard label="Artists" value={status?.totalArtists ?? 0} icon={<Person sx={{ color: '#00E676' }} />} color="#00E676" /></Grid>
                <Grid size={{ xs: 6, md: 3 }}><StatCard label="Playlists" value={status?.totalPlaylists ?? 0} icon={<QueueMusic sx={{ color: '#FFD600' }} />} color="#FFD600" /></Grid>
            </Grid>

            <Grid container spacing={2.5}>
                <Grid size={{ xs: 12, md: 6 }}>
                    <Card>
                        <CardContent>
                            <Typography variant="h6" gutterBottom>Server Info</Typography>
                            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between' }}><Typography variant="body2" color="text.secondary">Version</Typography><Typography variant="body2">{status?.version}</Typography></Box>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between' }}><Typography variant="body2" color="text.secondary">Library Roots</Typography><Typography variant="body2">{status?.libraryRootCount}</Typography></Box>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between' }}><Typography variant="body2" color="text.secondary">Genres</Typography><Typography variant="body2">{status?.totalGenres}</Typography></Box>
                                <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                                    <Typography variant="body2" color="text.secondary">Scan Status</Typography>
                                    <Chip size="small" label={status?.currentScanStatus ?? 'Idle'} color={status?.currentScanStatus === 'Running' ? 'warning' : 'success'} sx={{ height: 22 }} />
                                </Box>
                            </Box>
                        </CardContent>
                    </Card>
                </Grid>
                <Grid size={{ xs: 12, md: 6 }}>
                    <Card>
                        <CardContent>
                            <Typography variant="h6" gutterBottom>Recent Scans</Typography>
                            {Array.isArray(scans) && scans.length > 0 ? scans.slice(0, 5).map((scan: any) => (
                                <Box key={scan.id} sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', py: 0.5 }}>
                                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                        <Refresh fontSize="small" color="action" />
                                        <Typography variant="body2">{scan.libraryRootName}</Typography>
                                    </Box>
                                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                        <Typography variant="caption" color="text.secondary">{scan.filesScanned} files</Typography>
                                        <Chip size="small" label={scan.status} sx={{ height: 20, fontSize: 11 }}
                                            color={scan.status === 'Completed' ? 'success' : scan.status === 'Failed' ? 'error' : 'default'} />
                                    </Box>
                                </Box>
                            )) : <Typography variant="body2" color="text.secondary">No scans yet</Typography>}
                        </CardContent>
                    </Card>
                </Grid>
            </Grid>
        </Box>
    );
};

export default DashboardPage;
