import { useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
    Box, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Divider, IconButton,
    List, ListItemButton, ListItemIcon, ListItemText, Menu, MenuItem, Stack, TextField, Tooltip, Typography,
} from '@mui/material';
import type { SxProps, Theme } from '@mui/material';
import {
    Add, Album, Favorite, FavoriteBorder, MoreVert, MusicNote, Pause, Person, PlayArrow, PlaylistAdd,
    PlaylistPlay, QueueMusic,
} from '@mui/icons-material';
import { artworkUrl, listeningApi, playlistsApi } from '../api/client';
import type { PlaylistItemDto, TrackListItemDto } from '../api/types';
import { errorMessage, notify } from './Feedback';
import { usePlayer, type Track } from './PlayerContext';

// ── Formatting and conversion ──

/** A length of time as m:ss, or h:mm:ss from an hour up. */
export const formatDuration = (seconds?: number | null): string => {
    if (!seconds || !Number.isFinite(seconds)) return '–:––';
    const whole = Math.floor(seconds);
    const h = Math.floor(whole / 3600), m = Math.floor((whole % 3600) / 60), s = whole % 60;
    return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
};

/** How long a set of tracks runs, for a header: "42 min" or "1 hr 5 min". */
export const formatRunningTime = (tracks: { durationSeconds?: number | null }[]): string => {
    const minutes = Math.round(tracks.reduce((sum, t) => sum + (t.durationSeconds ?? 0), 0) / 60);
    return minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)} hr ${minutes % 60} min`;
};

export const toTrack = (t: TrackListItemDto): Track => ({
    id: t.id, title: t.title, artistName: t.artistName, albumTitle: t.albumTitle, artworkId: t.artworkId,
    durationSeconds: t.durationSeconds, albumId: t.albumId, artistId: t.artistId,
});

export const playlistItemToTrack = (i: PlaylistItemDto): Track => ({
    id: i.trackId, title: i.trackTitle, artistName: i.artistName, albumTitle: i.albumTitle,
    artworkId: i.artworkId, durationSeconds: i.durationSeconds, albumId: i.albumId,
});

// ── Cover art ──

const PLACEHOLDERS = { album: Album, artist: Person, track: MusicNote };

/**
 * A square cover that fills the width it is given. One component, so a missing cover
 * looks the same wherever it appears.
 */
export function Cover({ artworkId, size = 320, kind = 'album', round = false, sx }: {
    artworkId?: string | null; size?: number; kind?: keyof typeof PLACEHOLDERS; round?: boolean; sx?: SxProps<Theme>;
}) {
    const Placeholder = PLACEHOLDERS[kind];
    const src = artworkUrl(artworkId, size);
    // A cover the server no longer has falls back to the placeholder, not a broken image.
    const [failedSrc, setFailed] = useState<string | null>(null);
    const failed = failedSrc === src;
    return <Box sx={[{
        width: '100%', aspectRatio: '1', flexShrink: 0, overflow: 'hidden', borderRadius: round ? '50%' : 0,
        display: 'grid', placeItems: 'center', bgcolor: 'rgba(124,77,255,.10)', containerType: 'inline-size',
    }, ...(Array.isArray(sx) ? sx : [sx])]}>
        {src && !failed
            ? <Box component="img" alt="" loading="lazy" src={src} onError={() => setFailed(src)} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
            // Sized from the box, so the icon is in proportion on a 32px thumbnail and a 240px cover alike.
            : <Placeholder sx={{ fontSize: '42cqw', color: 'rgba(255,255,255,.16)' }} />}
    </Box>;
}

// ── Favourites ──

export function useFavorites() {
    const qc = useQueryClient();
    const query = useQuery({ queryKey: ['favorite-ids'], queryFn: () => listeningApi.favoriteIds().then(r => r.data), staleTime: 60_000 });
    const ids = useMemo(() => new Set(query.data ?? []), [query.data]);
    const mutation = useMutation({
        meta: { quiet: true },
        mutationFn: ({ trackId, favorite }: { trackId: string; favorite: boolean }) =>
            favorite ? listeningApi.addFavorite(trackId) : listeningApi.removeFavorite(trackId),
        // Shown at once; put back if the server says no.
        onMutate: async ({ trackId, favorite }) => {
            await qc.cancelQueries({ queryKey: ['favorite-ids'] });
            const before = qc.getQueryData<string[]>(['favorite-ids']);
            qc.setQueryData<string[]>(['favorite-ids'], current =>
                favorite ? [...(current ?? []), trackId] : (current ?? []).filter(id => id !== trackId));
            return { before };
        },
        onError: (error, _variables, context) => {
            qc.setQueryData(['favorite-ids'], context?.before);
            notify(errorMessage(error), 'error');
        },
        onSettled: () => {
            void qc.invalidateQueries({ queryKey: ['favorite-ids'] });
            void qc.invalidateQueries({ queryKey: ['favorites'] });
        },
    });
    return { isFavorite: (trackId: string) => ids.has(trackId), setFavorite: (trackId: string, favorite: boolean) => mutation.mutate({ trackId, favorite }) };
}

export function FavoriteButton({ track, size = 'small' }: { track: Pick<Track, 'id' | 'title'>; size?: 'small' | 'medium' }) {
    const { isFavorite, setFavorite } = useFavorites();
    const on = isFavorite(track.id);
    const label = on ? `Remove ${track.title} from favourites` : `Add ${track.title} to favourites`;
    return <Tooltip title={on ? 'Remove from favourites' : 'Add to favourites'}>
        <IconButton size={size} aria-label={label} aria-pressed={on} onClick={() => setFavorite(track.id, !on)}>
            {on ? <Favorite fontSize="small" sx={{ color: '#FF6B8B' }} /> : <FavoriteBorder fontSize="small" />}
        </IconButton>
    </Tooltip>;
}

// ── Adding to a playlist ──

export function AddToPlaylistDialog({ tracks, onClose }: { tracks: Track[] | null; onClose: () => void }) {
    const qc = useQueryClient();
    const [name, setName] = useState('');
    const playlists = useQuery({ queryKey: ['playlists'], queryFn: () => playlistsApi.getAll().then(r => r.data), enabled: !!tracks });
    const add = useMutation({
        meta: { quiet: true },
        mutationFn: async (target: { id?: string; name: string }) => {
            const id = target.id ?? (await playlistsApi.create({ name: target.name })).data.id;
            // One at a time, so they land in the playlist in the order shown.
            for (const track of tracks ?? []) await playlistsApi.addTrack(id, track.id);
            return target.name;
        },
        onSuccess: playlistName => {
            const count = tracks?.length ?? 0;
            notify(count === 1 ? `Added to ${playlistName}.` : `Added ${count} tracks to ${playlistName}.`);
            void qc.invalidateQueries({ queryKey: ['playlists'] });
            void qc.invalidateQueries({ queryKey: ['playlist'] });
            setName('');
            onClose();
        },
        onError: error => notify(errorMessage(error), 'error'),
    });
    const what = tracks?.length === 1 ? tracks[0].title : `${tracks?.length ?? 0} tracks`;
    return <Dialog open={!!tracks} onClose={onClose} maxWidth="xs" fullWidth>
        <DialogTitle>Add to playlist<Typography variant="body2" color="text.secondary" noWrap>{what}</Typography></DialogTitle>
        <DialogContent dividers sx={{ p: 0 }}>
            {playlists.isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}><CircularProgress size={24} /></Box>}
            {playlists.isError && <Typography color="error" sx={{ p: 2 }}>{errorMessage(playlists.error)}</Typography>}
            <List disablePadding>
                {playlists.data?.map(p => <ListItemButton key={p.id} disabled={add.isPending} onClick={() => add.mutate({ id: p.id, name: p.name })}>
                    <ListItemIcon sx={{ minWidth: 40 }}><QueueMusic fontSize="small" /></ListItemIcon>
                    <ListItemText primary={p.name} secondary={`${p.trackCount} tracks`} />
                </ListItemButton>)}
            </List>
            {playlists.data?.length === 0 && <Typography color="text.secondary" sx={{ px: 2, pt: 2 }}>You have no playlists yet. Name one to create it.</Typography>}
            <Stack direction="row" spacing={1} sx={{ p: 2 }} component="form"
                onSubmit={event => { event.preventDefault(); if (name.trim()) add.mutate({ name: name.trim() }); }}>
                <TextField size="small" fullWidth label="New playlist" value={name} onChange={e => setName(e.target.value)} />
                <Button type="submit" variant="contained" startIcon={<Add />} disabled={!name.trim() || add.isPending}>Create</Button>
            </Stack>
        </DialogContent>
        <DialogActions><Button onClick={onClose}>Cancel</Button></DialogActions>
    </Dialog>;
}

// ── The "more" menu on a track ──

export function TrackMenu({ track, children }: { track: Track; children?: (close: () => void) => ReactNode }) {
    const player = usePlayer();
    const [anchor, setAnchor] = useState<HTMLElement | null>(null);
    const [adding, setAdding] = useState(false);
    const close = () => setAnchor(null);
    return <>
        <IconButton size="small" aria-label={`More actions for ${track.title}`} aria-haspopup="menu" onClick={event => setAnchor(event.currentTarget)}>
            <MoreVert fontSize="small" />
        </IconButton>
        <Menu anchorEl={anchor} open={!!anchor} onClose={close}>
            {!player.sharedRoom && <MenuItem onClick={() => { player.playNext(track); notify('Playing next.'); close(); }}>
                <ListItemIcon><PlaylistPlay fontSize="small" /></ListItemIcon>Play next
            </MenuItem>}
            {!player.sharedRoom && <MenuItem onClick={() => { player.addToQueue([track]); notify('Added to the queue.'); close(); }}>
                <ListItemIcon><QueueMusic fontSize="small" /></ListItemIcon>Add to queue
            </MenuItem>}
            <MenuItem onClick={() => { setAdding(true); close(); }}>
                <ListItemIcon><PlaylistAdd fontSize="small" /></ListItemIcon>Add to playlist…
            </MenuItem>
            {(track.albumId || track.artistId) && <Divider />}
            {track.albumId && <MenuItem component={Link} to={`/albums/${track.albumId}`} onClick={close}>
                <ListItemIcon><Album fontSize="small" /></ListItemIcon>Go to album
            </MenuItem>}
            {track.artistId && <MenuItem component={Link} to={`/artists/${track.artistId}`} onClick={close}>
                <ListItemIcon><Person fontSize="small" /></ListItemIcon>Go to artist
            </MenuItem>}
            {children?.(close)}
        </Menu>
        <AddToPlaylistDialog tracks={adding ? [track] : null} onClose={() => setAdding(false)} />
    </>;
}

// ── A list of tracks ──

const linkSx = { color: 'inherit', textDecoration: 'none', '&:hover': { textDecoration: 'underline', color: 'primary.light' } } as const;

/**
 * Tracks as rows: play, title and artist, album, favourite, length, more. Used wherever
 * tracks are listed, so every list plays, queues and favourites the same way.
 */
export function TrackList({ tracks, numbers, showCover = false, showAlbum = true, menuExtra, empty = 'No tracks.' }: {
    tracks: Track[];
    /** What to show in the first column when a row is not playing: track numbers, or positions. */
    numbers?: (track: Track, index: number) => ReactNode;
    showCover?: boolean;
    showAlbum?: boolean;
    /** Extra menu items for a row, such as "Remove from playlist". */
    menuExtra?: (track: Track, index: number, close: () => void) => ReactNode;
    empty?: string;
}) {
    const player = usePlayer();
    if (tracks.length === 0) return <Typography color="text.secondary" textAlign="center" sx={{ py: 5 }}>{empty}</Typography>;
    return <Box role="list">
        {tracks.map((track, index) => {
            const current = !player.sharedRoom && player.currentTrack?.id === track.id;
            const playing = current && player.isPlaying;
            return <Stack key={`${track.id}-${index}`} role="listitem" direction="row" alignItems="center" spacing={1.5}
                sx={{ px: { xs: 1, sm: 2 }, py: 0.75, borderBottom: '1px solid', borderColor: 'divider', '&:last-of-type': { borderBottom: 0 },
                    bgcolor: current ? 'rgba(124,77,255,.10)' : undefined, '&:hover': { bgcolor: current ? 'rgba(124,77,255,.14)' : 'rgba(255,255,255,.03)' },
                    '&:hover .row-number, &:focus-within .row-number': { opacity: 0 }, '&:hover .row-play, &:focus-within .row-play': { opacity: 1 } }}>
                {/* The number and the play button share one cell. The button is only faded out, never
                    removed, so it can still be reached with the keyboard. */}
                <Box sx={{ width: 36, flexShrink: 0, display: 'grid', placeItems: 'center', '& > *': { gridArea: '1 / 1' } }}>
                    {numbers && !current && <Typography className="row-number" variant="body2" color="text.secondary" aria-hidden sx={{ gridArea: '1 / 1', pointerEvents: 'none' }}>{numbers(track, index)}</Typography>}
                    <IconButton className="row-play" size="small" sx={{ opacity: numbers && !current ? 0 : 1, color: current ? 'primary.light' : undefined }}
                        aria-label={playing ? `Pause ${track.title}` : `Play ${track.title}`}
                        onClick={() => current ? (playing ? player.pause() : player.resume()) : player.play(track, tracks)}>
                        {playing ? <Pause fontSize="small" /> : <PlayArrow fontSize="small" />}
                    </IconButton>
                </Box>
                {showCover && <Cover artworkId={track.artworkId} size={160} kind="track" sx={{ width: 40, borderRadius: 1 }} />}
                <Box sx={{ minWidth: 0, flex: 2 }}>
                    <Typography variant="body2" fontWeight={600} noWrap sx={{ color: current ? 'primary.light' : 'text.primary' }} title={track.title}>{track.title}</Typography>
                    <Typography variant="caption" color="text.secondary" noWrap component="div">
                        {track.artistId
                            ? <Box component={Link} to={`/artists/${track.artistId}`} sx={linkSx}>{track.artistName || 'Unknown artist'}</Box>
                            : track.artistName || 'Unknown artist'}
                    </Typography>
                </Box>
                {showAlbum && <Typography variant="body2" color="text.secondary" noWrap sx={{ minWidth: 0, flex: 1.4, display: { xs: 'none', md: 'block' } }}>
                    {track.albumId
                        ? <Box component={Link} to={`/albums/${track.albumId}`} sx={linkSx}>{track.albumTitle || '—'}</Box>
                        : track.albumTitle || '—'}
                </Typography>}
                <Box sx={{ display: { xs: 'none', sm: 'block' } }}><FavoriteButton track={track} /></Box>
                <Typography variant="caption" color="text.secondary" sx={{ width: 44, textAlign: 'right', flexShrink: 0 }}>{formatDuration(track.durationSeconds)}</Typography>
                <TrackMenu track={track}>{menuExtra ? close => menuExtra(track, index, close) : undefined}</TrackMenu>
            </Stack>;
        })}
    </Box>;
}
