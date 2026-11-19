import React, { useMemo } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Box, Button, Card, Chip, IconButton, ListItemIcon, MenuItem, Stack, Typography } from '@mui/material';
import { ArrowBack, Delete, PlaylistRemove, QueueMusic } from '@mui/icons-material';
import { playlistsApi } from '../api/client';
import { PlayActions } from '../components/Cards';
import { errorMessage, notify, useConfirm } from '../components/Feedback';
import { TrackList, formatRunningTime, playlistItemToTrack } from '../components/Music';
import { EmptyState, PageLoading, QueryError } from '../components/PageParts';

const PlaylistDetailPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const qc = useQueryClient();
    const confirm = useConfirm();
    const { data: playlist, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['playlist', id],
        queryFn: () => playlistsApi.getById(id!).then(r => r.data),
        enabled: !!id,
    });
    const tracks = useMemo(() => (playlist?.items ?? []).map(playlistItemToTrack), [playlist]);

    const refresh = () => {
        void qc.invalidateQueries({ queryKey: ['playlist', id] });
        void qc.invalidateQueries({ queryKey: ['playlists'] });
    };
    const remove = useMutation({
        meta: { quiet: true },
        mutationFn: (itemId: string) => playlistsApi.removeItem(id!, itemId),
        onSuccess: () => { notify('Removed from the playlist.'); refresh(); },
        onError: err => notify(errorMessage(err), 'error'),
    });
    const deletePlaylist = useMutation({
        meta: { quiet: true },
        mutationFn: () => playlistsApi.delete(id!),
        onSuccess: () => { void qc.invalidateQueries({ queryKey: ['playlists'] }); navigate('/playlists'); },
        onError: err => notify(errorMessage(err), 'error'),
    });

    if (isLoading) return <PageLoading />;
    if (isError) return <QueryError error={error} retry={refetch} />;
    if (!playlist) return <Typography>Playlist not found</Typography>;

    return (
        <Box>
            <IconButton aria-label="Back to playlists" onClick={() => navigate('/playlists')} sx={{ mb: 2 }}><ArrowBack /></IconButton>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3} sx={{ mb: 4 }} alignItems={{ xs: 'flex-start', sm: 'flex-end' }}>
                <Box sx={{ width: { xs: 120, sm: 160 }, aspectRatio: '1', borderRadius: 3, flexShrink: 0, display: 'grid', placeItems: 'center',
                    background: 'linear-gradient(135deg, rgba(124,77,255,.45), rgba(0,229,255,.18))' }}>
                    <QueueMusic sx={{ fontSize: 64, color: 'rgba(255,255,255,.75)' }} />
                </Box>
                <Box sx={{ minWidth: 0, flex: 1 }}>
                    <Typography variant="caption" color="text.secondary" textTransform="uppercase" letterSpacing={1}>Playlist</Typography>
                    <Typography variant="h4" component="h1" fontWeight={700} sx={{ overflowWrap: 'anywhere' }}>{playlist.name}</Typography>
                    {playlist.description && <Typography color="text.secondary" sx={{ mt: 0.5 }}>{playlist.description}</Typography>}
                    <Stack direction="row" spacing={1} alignItems="center" sx={{ my: 1.5 }}>
                        <Typography variant="body2" color="text.secondary">
                            {tracks.length} {tracks.length === 1 ? 'track' : 'tracks'}{tracks.length > 0 ? ` · ${formatRunningTime(tracks)}` : ''}
                        </Typography>
                        {playlist.isPublic && <Chip size="small" label="Public" color="primary" sx={{ height: 20 }} />}
                    </Stack>
                    <PlayActions tracks={tracks}>
                        <Button color="error" startIcon={<Delete />} disabled={deletePlaylist.isPending}
                            onClick={async () => {
                                if (await confirm({ title: `Delete “${playlist.name}”?`, description: 'This permanently removes the playlist. The music in your library stays available.' }))
                                    deletePlaylist.mutate();
                            }}>Delete</Button>
                    </PlayActions>
                </Box>
            </Stack>

            {tracks.length === 0
                ? <EmptyState title="Nothing in this playlist yet"
                    description="Open the ⋮ menu on any track, or use the playlist button on an album, and choose Add to playlist." />
                : <Card>
                    <TrackList tracks={tracks} showCover numbers={(_, index) => index + 1}
                        menuExtra={(_, index, close) => <MenuItem onClick={() => { remove.mutate(playlist.items[index].id); close(); }}>
                            <ListItemIcon><PlaylistRemove fontSize="small" /></ListItemIcon>Remove from this playlist
                        </MenuItem>} />
                </Card>}
        </Box>
    );
};

export default PlaylistDetailPage;
