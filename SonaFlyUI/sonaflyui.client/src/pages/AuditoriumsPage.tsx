import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Alert, Avatar, Box, Button, Card, CardContent, Chip, Dialog, DialogTitle, DialogContent, DialogActions, Grid, IconButton, List, ListItem, ListItemText, Stack, TextField, Typography } from '@mui/material';
import { Add, Delete, MeetingRoom, People, QueueMusic, SkipNext, Logout } from '@mui/icons-material';
import { auditoriumsApi, browseApi } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useAuditorium, type Room } from '../components/AuditoriumContext';
import { useConfirm, errorMessage } from '../components/Feedback';
import { PageHeader, PageLoading, QueryError, EmptyState } from '../components/PageParts';
import { useSearchFilter } from '../hooks/useSearchFilter';
import type { Track } from '../components/PlayerContext';

/**
 * A library full of discographies holds the same song on several albums. Showing only the
 * title and artist renders those as identical rows, so the album and running time are what
 * make them tellable apart — and what let someone pick the right one to queue.
 */
const fmt = (seconds?: number | null) =>
    seconds == null || !Number.isFinite(seconds)
        ? null
        : `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;

const trackDetail = (track: Track) =>
    [track.artistName, track.albumTitle, fmt(track.durationSeconds)].filter(Boolean).join(' · ');

export default function AuditoriumsPage() {
    const { isAdmin, user } = useAuth();
    const auditorium = useAuditorium();
    const confirm = useConfirm();
    const qc = useQueryClient();
    const [open, setOpen] = useState(false);
    const [name, setName] = useState('');
    const { input, setInput, value: query } = useSearchFilter();
    const rooms = useQuery({ queryKey: ['auditoriums'], queryFn: () => auditoriumsApi.getAll().then(r => r.data as Room[]), refetchInterval: 5000 });
    const results = useQuery({ queryKey: ['room-search', query], queryFn: () => browseApi.search(query, 15).then(r => r.data.tracks as Track[]), enabled: !!auditorium.room && query.length >= 2 });
    const create = useMutation({ mutationFn: () => auditoriumsApi.create(name.trim()), meta: { successMessage: 'Auditorium created.' }, onSuccess: () => { qc.invalidateQueries({ queryKey: ['auditoriums'] }); setOpen(false); setName(''); } });
    const remove = useMutation({ mutationFn: (id: string) => auditoriumsApi.delete(id), meta: { successMessage: 'Auditorium deleted.' }, onSuccess: () => qc.invalidateQueries({ queryKey: ['auditoriums'] }) });
    const { room, snapshot, connectionStatus, busy } = auditorium;
    const connected = connectionStatus === 'Connected';
    const canSkip = !!snapshot?.currentTrackId && (isAdmin || snapshot.startedByUserId === user?.id);
    return <Box>
        <PageHeader title="Auditoriums" subtitle="Listen together. Everyone in the room shares the same music."
            action={isAdmin && <Button variant="contained" startIcon={<Add />} onClick={() => { create.reset(); setOpen(true); }}>Create room</Button>} />
        {auditorium.error && <Alert severity="error" sx={{ mb: 2 }}>{auditorium.error}</Alert>}
        {auditorium.trackUnavailable && <Alert severity="info" sx={{ mb: 2 }}>
            This track isn’t available on your account, so it won’t play for you. You’ll rejoin the room automatically at the next track.
        </Alert>}
        {room && <Card sx={{ mb: 3, borderColor: 'primary.main' }}><CardContent>
            <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={2} mb={2}>
                <Box><Stack direction="row" gap={1} alignItems="center"><MeetingRoom color="primary" /><Typography variant="h6">{room.name}</Typography><Chip size="small" label={connectionStatus} color={connected ? 'success' : 'warning'} /></Stack>
                    <Typography color="text.secondary" variant="body2" mt={1}>{snapshot?.currentTrackTitle ? `Now playing: ${snapshot.currentTrackTitle} · ${snapshot.currentArtistName || 'Unknown artist'}` : 'The room is quiet. Add a track to start listening.'}</Typography></Box>
                <Stack direction="row" gap={1} alignItems="center">
                    {!connected && <Button disabled={busy} onClick={() => auditorium.join(room)}>Rejoin</Button>}
                    {canSkip && <Button disabled={!connected || busy} startIcon={<SkipNext />} onClick={() => auditorium.command('SkipTrack')}>Skip track</Button>}
                    <Button startIcon={<Logout />} onClick={auditorium.leave}>Leave</Button>
                </Stack>
            </Stack>
            <Stack direction="row" gap={1} flexWrap="wrap" mb={3}>{snapshot?.activeUsers.map(person => <Chip key={person.userId} avatar={<Avatar>{person.displayName[0]}</Avatar>} label={person.displayName} />)}</Stack>
            <Grid container spacing={3}>
                <Grid size={{ xs: 12, md: 6 }}><Typography variant="subtitle1" mb={1}>Shared queue · {snapshot?.queue.length ?? 0}/100</Typography>
                    {snapshot?.queue.length ? <List dense>{snapshot.queue.map((item, index) => <ListItem key={item.id} disableGutters secondaryAction={(isAdmin || item.queuedByUserId === user?.id) && <IconButton aria-label={`Remove ${item.title} from room queue`} disabled={busy || !connected} onClick={() => auditorium.command('RemoveFromQueue', item.id)}><Delete fontSize="small" /></IconButton>}>
                        <ListItemText primary={`${index + 1}. ${item.title}`} secondary={`${item.artistName || 'Unknown artist'} · Added by ${item.queuedByUserName}`} sx={{ pr: 4 }} />
                    </ListItem>)}</List> : <Typography color="text.secondary" variant="body2">Nothing queued yet. Search for a track to add.</Typography>}
                </Grid>
                <Grid size={{ xs: 12, md: 6 }}><TextField fullWidth label="Find a track for this room" value={input} onChange={e => setInput(e.target.value)} />
                    {results.isError ? <QueryError error={results.error} retry={results.refetch} /> : results.isFetching ? <Typography color="text.secondary" mt={2}>Searching…</Typography> : query.length >= 2 && <List dense>{results.data?.map(track => <ListItem key={track.id} disableGutters secondaryAction={<IconButton aria-label={`Queue ${track.title}`} disabled={busy || !connected || (snapshot?.queue.length ?? 0) >= 100} onClick={() => auditorium.command('QueueTrack', track.id)}><Add /></IconButton>}><ListItemText primary={track.title} secondary={trackDetail(track)} sx={{ pr: 4 }} /></ListItem>)}{results.data?.length === 0 && <Typography color="text.secondary" mt={2}>No matching tracks.</Typography>}</List>}
                </Grid>
            </Grid>
            <Typography variant="caption" color="text.secondary">Playback stays in sync as you browse. Playing personal music automatically leaves this room.</Typography>
        </CardContent></Card>}
        {rooms.isLoading ? <PageLoading /> : rooms.isError ? <QueryError error={rooms.error} retry={rooms.refetch} /> : rooms.data?.length === 0 ? <EmptyState title="A place to listen together" description={isAdmin ? 'Create a room, invite listeners, and build a shared queue.' : 'Ask your administrator to create an auditorium.'} /> :
            <Grid container spacing={2}>{rooms.data?.map(item => <Grid key={item.id} size={{ xs: 12, sm: 6, lg: 4 }}><Card><CardContent>
                <Stack direction="row" justifyContent="space-between" alignItems="center"><MeetingRoom sx={{ fontSize: 32, color: 'primary.light' }} />{isAdmin && <IconButton aria-label={`Delete ${item.name}`} disabled={remove.isPending} onClick={async () => { if (await confirm({ title: `Delete “${item.name}”?`, description: 'The room will close for all listeners. Music files will not be deleted.' })) { if (room?.id === item.id) await auditorium.leave(); remove.mutate(item.id); } }}><Delete fontSize="small" /></IconButton>}</Stack>
                <Typography variant="h6" mt={1}>{item.name}</Typography><Chip icon={<People />} label={`${item.activeUserCount} listening`} size="small" sx={{ my: 1.5 }} />
                <Typography noWrap color="text.secondary" variant="body2" mb={2}>{item.nowPlaying || 'Ready for your first track'}</Typography>
                <Button fullWidth variant={room?.id === item.id ? 'outlined' : 'contained'} startIcon={<QueueMusic />} disabled={busy || room?.id === item.id} onClick={() => auditorium.join(item)}>{room?.id === item.id ? 'You’re listening here' : 'Join room'}</Button>
            </CardContent></Card></Grid>)}</Grid>}
        <Dialog open={open} onClose={() => { if (!create.isPending) setOpen(false); }} maxWidth="xs" fullWidth>
            <DialogTitle>Create auditorium</DialogTitle><Box component="form" onSubmit={e => { e.preventDefault(); create.mutate(); }}><DialogContent>
                {create.isError && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(create.error)}</Alert>}<TextField autoFocus fullWidth required label="Room name" value={name} onChange={e => setName(e.target.value)} slotProps={{ htmlInput: { maxLength: 200 } }} />
            </DialogContent><DialogActions><Button disabled={create.isPending} onClick={() => setOpen(false)}>Cancel</Button><Button type="submit" variant="contained" disabled={!name.trim() || create.isPending}>{create.isPending ? 'Creating…' : 'Create room'}</Button></DialogActions></Box>
        </Dialog>
    </Box>;
}
