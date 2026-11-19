import React, { useMemo } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Box, Card, Chip, IconButton, Stack, Typography } from '@mui/material';
import { ArrowBack } from '@mui/icons-material';
import { browseApi } from '../api/client';
import { PlayActions } from '../components/Cards';
import { Cover, TrackList, formatRunningTime, toTrack } from '../components/Music';
import { PageLoading, QueryError } from '../components/PageParts';

const AlbumDetailPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const { data: album, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['album', id],
        queryFn: () => browseApi.albumById(id!).then(r => r.data),
        enabled: !!id,
    });

    // The album's own title and cover, so the player shows them for every track.
    const tracks = useMemo(() => (album?.tracks ?? []).map(t => ({
        ...toTrack(t), albumTitle: album!.title, albumId: album!.id, artworkId: album!.artworkId,
    })), [album]);

    if (isLoading) return <PageLoading />;
    if (isError) return <QueryError error={error} retry={refetch} />;
    if (!album) return <Typography>Album not found</Typography>;

    const multiDisc = (album.discCount ?? 1) > 1;
    const numbers = new Map(album.tracks.map(t => [t.id, `${multiDisc && t.discNumber ? `${t.discNumber}-` : ''}${t.trackNumber ?? '–'}`]));

    return (
        <Box>
            <IconButton aria-label="Back" onClick={() => navigate(-1)} sx={{ mb: 2 }}><ArrowBack /></IconButton>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3} sx={{ mb: 4 }} alignItems={{ xs: 'flex-start', sm: 'flex-end' }}>
                <Cover artworkId={album.artworkId} size={640} sx={{ width: { xs: 200, sm: 240 }, borderRadius: 3 }} />
                {/* minWidth lets a long title wrap inside the row rather than run off the page. */}
                <Box sx={{ minWidth: 0, flex: 1 }}>
                    <Typography variant="caption" color="text.secondary" textTransform="uppercase" letterSpacing={1}>Album</Typography>
                    <Typography variant="h4" component="h1" fontWeight={700} sx={{ mb: 0.5, overflowWrap: 'anywhere' }}>{album.title}</Typography>
                    <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap" useFlexGap sx={{ mb: 2 }}>
                        {album.artistName && (album.artistId
                            ? <Typography component={Link} to={`/artists/${album.artistId}`} variant="body1" fontWeight={500}
                                sx={{ color: 'primary.light', textDecoration: 'none', '&:hover': { textDecoration: 'underline' } }}>{album.artistName}</Typography>
                            : <Typography variant="body1" fontWeight={500}>{album.artistName}</Typography>)}
                        {album.year && <Chip label={album.year} size="small" sx={{ height: 22 }} />}
                        {album.genreSummary && <Chip label={album.genreSummary} size="small" variant="outlined" sx={{ height: 22 }} />}
                        <Typography variant="body2" color="text.secondary">
                            {tracks.length} {tracks.length === 1 ? 'track' : 'tracks'} · {formatRunningTime(tracks)}
                        </Typography>
                    </Stack>
                    <PlayActions tracks={tracks} />
                </Box>
            </Stack>

            <Card><TrackList tracks={tracks} showAlbum={false} numbers={track => numbers.get(track.id)} /></Card>
        </Box>
    );
};

export default AlbumDetailPage;
