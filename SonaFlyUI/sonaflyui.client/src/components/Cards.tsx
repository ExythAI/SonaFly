import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Button, Card, CardActionArea, CardContent, Grid, IconButton, Stack, Tooltip, Typography } from '@mui/material';
import { PlayArrow, PlaylistAdd, QueueMusic, Shuffle } from '@mui/icons-material';
import type { AlbumDto, ArtistDto } from '../api/types';
import { AddToPlaylistDialog, Cover } from './Music';
import { notify } from './Feedback';
import { usePlayer, type Track } from './PlayerContext';

type AlbumLike = Pick<AlbumDto, 'id' | 'title'> & Partial<Pick<AlbumDto, 'artistName' | 'year' | 'artworkId' | 'trackCount'>>;

/** An album as a tile: cover, title, and one line beneath it. */
export function AlbumCard({ album, caption }: { album: AlbumLike; caption?: string }) {
    const line = caption ?? [album.artistName || 'Various artists', album.year].filter(Boolean).join(' · ');
    return <Card sx={{ height: '100%' }}>
        <CardActionArea component={Link} to={`/albums/${album.id}`} sx={{ height: '100%', display: 'flex', flexDirection: 'column', alignItems: 'stretch', justifyContent: 'flex-start' }}>
            <Cover artworkId={album.artworkId} />
            <CardContent sx={{ p: 1.5, width: '100%', minWidth: 0 }}>
                <Typography variant="subtitle2" noWrap title={album.title}>{album.title}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap component="div">{line}</Typography>
            </CardContent>
        </CardActionArea>
    </Card>;
}

/** Albums in the grid used on every screen that lists them. */
export function AlbumGrid({ albums, caption }: { albums: AlbumLike[]; caption?: (album: AlbumLike) => string }) {
    return <Grid container spacing={2}>
        {albums.map(album => <Grid key={album.id} size={{ xs: 6, sm: 4, md: 3, lg: 2 }}>
            <AlbumCard album={album} caption={caption?.(album)} />
        </Grid>)}
    </Grid>;
}

/** An artist as a tile, pictured by one of their album covers when they have no photograph. */
export function ArtistCard({ artist }: { artist: ArtistDto }) {
    const albums = `${artist.albumCount} ${artist.albumCount === 1 ? 'album' : 'albums'}`;
    const tracks = `${artist.trackCount} ${artist.trackCount === 1 ? 'track' : 'tracks'}`;
    return <Card sx={{ height: '100%' }}>
        <CardActionArea component={Link} to={`/artists/${artist.id}`} sx={{ height: '100%' }}>
            <CardContent sx={{ textAlign: 'center', py: 2.5 }}>
                <Cover artworkId={artist.artworkId} kind="artist" round size={160} sx={{ width: 88, mx: 'auto', mb: 1.5 }} />
                <Typography variant="subtitle2" noWrap title={artist.name}>{artist.name}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap component="div">
                    {artist.albumCount > 0 ? `${albums} · ${tracks}` : tracks}
                </Typography>
            </CardContent>
        </CardActionArea>
    </Card>;
}

/**
 * The buttons at the top of anything playable: an album, a playlist, the favourites.
 * Hidden while listening in a shared room, where the room decides what plays.
 */
export function PlayActions({ tracks, children }: { tracks: Track[]; children?: ReactNode }) {
    const player = usePlayer();
    const [adding, setAdding] = useState(false);
    const none = tracks.length === 0;
    if (player.sharedRoom) return <Stack direction="row" spacing={1} alignItems="center">{children}</Stack>;
    return <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
        <Button variant="contained" startIcon={<PlayArrow />} disabled={none} onClick={() => player.play(tracks[0], tracks)}>Play</Button>
        <Button variant="outlined" startIcon={<Shuffle />} disabled={none} onClick={() => player.playShuffled(tracks)}>Shuffle</Button>
        <Tooltip title="Add to queue"><span>
            <IconButton aria-label="Add all to the queue" disabled={none} onClick={() => { player.addToQueue(tracks); notify(`Added ${tracks.length} ${tracks.length === 1 ? 'track' : 'tracks'} to the queue.`); }}><QueueMusic /></IconButton>
        </span></Tooltip>
        <Tooltip title="Add to playlist"><span>
            <IconButton aria-label="Add all to a playlist" disabled={none} onClick={() => setAdding(true)}><PlaylistAdd /></IconButton>
        </span></Tooltip>
        {children}
        <AddToPlaylistDialog tracks={adding ? tracks : null} onClose={() => setAdding(false)} />
    </Stack>;
}
