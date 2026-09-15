import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Alert, Box, Button, Card, CardActionArea, CardContent, Chip, Grid, LinearProgress, Stack, Typography } from '@mui/material';
import { ArrowForward, FolderOpen, Refresh, Album } from '@mui/icons-material';
import { systemApi, scansApi, libraryRootsApi, browseApi, artworkUrl } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { PageHeader, PageLoading, QueryError, EmptyState } from '../components/PageParts';

type Scan = { id: string; libraryRootName: string; status: string; filesScanned: number; filesAdded: number; errorsCount: number; errorSummary?: string; completedUtc?: string };
type AlbumItem = { id: string; title: string; artistName?: string; artworkId?: string };
export default function DashboardPage() {
    const { isAdmin, user } = useAuth();
    const qc = useQueryClient();
    const statusQuery = useQuery({ queryKey: ['system-status'], queryFn: () => systemApi.status().then(r => r.data), refetchInterval: 5000 });
    const scansQuery = useQuery({ queryKey: ['recent-scans'], queryFn: () => scansApi.getAll().then(r => r.data as Scan[]), enabled: isAdmin, refetchInterval: 5000 });
    const albumsQuery = useQuery({ queryKey: ['home-albums'], queryFn: () => browseApi.albums(1, 6).then(r => r.data.items as AlbumItem[]) });
    const scan = useMutation({ mutationFn: async () => {
        const roots = (await libraryRootsApi.getAll()).data as { id: string; isEnabled: boolean }[];
        const enabled = roots.filter(r => r.isEnabled);
        if (!enabled.length) throw new Error('Add and enable a music folder before starting a scan.');
        for (const root of enabled) await libraryRootsApi.triggerScan(root.id);
    }, meta: { successMessage: 'Library scans queued.' }, onSuccess: () => { qc.invalidateQueries({ queryKey: ['recent-scans'] }); qc.invalidateQueries({ queryKey: ['system-status'] }); } });
    if (statusQuery.isLoading) return <PageLoading />;
    if (statusQuery.isError) return <QueryError error={statusQuery.error} retry={statusQuery.refetch} />;
    const status = statusQuery.data;
    // isLoading is false on a background refetch, so data can still be absent here.
    if (!status) return <PageLoading />;
    const scans = scansQuery.data ?? [];
    const active = scans.filter(s => ['Running', 'Queued'].includes(s.status));
    const latest = scans[0];
    const successful = scans.find(s => s.status === 'Completed');
    const stats = [
        { name: 'Tracks', value: status.totalTracks, path: '/tracks' },
        { name: 'Albums', value: status.totalAlbums, path: '/albums' },
        { name: 'Artists', value: status.totalArtists, path: '/artists' },
        { name: 'Playlists', value: status.totalPlaylists, path: '/playlists' },
    ];
    return <Box>
        <PageHeader title={`Welcome back${user?.displayName ? `, ${user.displayName}` : ''}`} subtitle="Your collection. Ready when you are."
            action={isAdmin && <><Button component={Link} to="/library-roots" startIcon={<FolderOpen />} variant="outlined">Music folders</Button>
                <Button startIcon={<Refresh />} variant="contained" disabled={scan.isPending || active.length > 0 || !status.libraryRootCount}
                    onClick={() => scan.mutate()}>{scan.isPending ? 'Queuing…' : active.length ? 'Scanning…' : 'Scan library'}</Button></>} />
        {status.totalTracks === 0 && <EmptyState title={isAdmin ? 'Bring your music in' : 'Your library is getting ready'}
            description={isAdmin ? 'Add a music folder and scan it. Your albums and tracks will appear here.' : 'Ask your administrator to add music to the server.'}
            action={isAdmin && <Button component={Link} to="/library-roots" variant="contained">Add a music folder</Button>} />}
        <Grid container spacing={2} sx={{ my: 2 }}>{stats.map(stat => <Grid key={stat.name} size={{ xs: 6, md: 3 }}>
            <Card><CardActionArea component={Link} to={stat.path}><CardContent>
                <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="body2" color="text.secondary">{stat.name}</Typography><ArrowForward sx={{ fontSize: 16, color: 'text.secondary' }} /></Stack>
                <Typography sx={{ fontSize: 32, letterSpacing: '-.04em', fontWeight: 700, mt: 1 }}>{Number(stat.value).toLocaleString()}</Typography>
            </CardContent></CardActionArea></Card>
        </Grid>)}</Grid>
        {isAdmin && <Card sx={{ my: 3 }}><CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}><Typography variant="h6">Library activity</Typography><Chip size="small" label={scansQuery.isError ? 'Unavailable' : active.length ? 'Scanning' : latest?.status === 'Failed' ? 'Needs attention' : successful ? 'Idle' : 'No scans yet'} color={scansQuery.isError ? 'default' : active.length ? 'warning' : 'default'} /></Stack>
            {scansQuery.isError ? <QueryError error={scansQuery.error} retry={scansQuery.refetch} /> : <>
                {active.map(job => <Box key={job.id} mb={2}><Stack direction="row" justifyContent="space-between" mb={1}><Typography variant="body2">{job.libraryRootName}</Typography><Typography variant="body2" color="text.secondary">{job.status} · {job.filesScanned.toLocaleString()} files checked</Typography></Stack><LinearProgress aria-label={`Scanning ${job.libraryRootName}`} /></Box>)}
                {latest?.status === 'Failed' && <Alert severity="error" sx={{ mb: 2 }} action={<Button component={Link} to="/library-roots" color="inherit">View folders</Button>}>{latest.libraryRootName}: {latest.errorSummary || 'The latest scan failed. Check the folder path and permissions.'}</Alert>}
                <Typography variant="body2" color="text.secondary">{successful?.completedUtc ? `Last successful scan: ${new Date(successful.completedUtc).toLocaleString()} · ${successful.filesAdded} tracks added` : 'No successful scans yet.'}</Typography>
            </>}
        </CardContent></Card>}
        <Stack direction="row" alignItems="center" justifyContent="space-between" my={3}><Typography variant="h6">From your library</Typography><Button component={Link} to="/albums" endIcon={<ArrowForward />}>All albums</Button></Stack>
        {albumsQuery.isError ? <QueryError error={albumsQuery.error} retry={albumsQuery.refetch} /> : <Grid container spacing={2}>{albumsQuery.data?.map(album => <Grid key={album.id} size={{ xs: 6, sm: 4, lg: 2 }}>
            <Card><CardActionArea component={Link} to={`/albums/${album.id}`}><Box sx={{ aspectRatio: '1', bgcolor: 'rgba(124,77,255,.08)', display: 'grid', placeItems: 'center' }}>
                {album.artworkId ? <Box component="img" alt="" src={artworkUrl(album.artworkId)} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} /> : <Album sx={{ fontSize: 56, color: 'text.disabled' }} />}
            </Box><CardContent sx={{ p: 1.5 }}><Typography noWrap variant="subtitle2">{album.title}</Typography><Typography noWrap variant="caption" color="text.secondary">{album.artistName || 'Various artists'}</Typography></CardContent></CardActionArea></Card>
        </Grid>)}</Grid>}
    </Box>;
}
