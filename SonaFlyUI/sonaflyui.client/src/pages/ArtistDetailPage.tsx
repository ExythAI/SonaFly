import React, { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useParams, useNavigate } from 'react-router-dom';
import { Box, Card, Chip, IconButton, Stack, Typography } from '@mui/material';
import { ArrowBack } from '@mui/icons-material';
import { browseApi } from '../api/client';
import { AlbumGrid, PlayActions } from '../components/Cards';
import { Cover, TrackList, toTrack } from '../components/Music';
import { PageLoading, QueryError } from '../components/PageParts';

const ArtistDetailPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();

    const { data: artist, isLoading: artistLoading, isError, error, refetch } = useQuery({
        queryKey: ['artist', id],
        queryFn: () => browseApi.artistById(id!).then(r => r.data),
        enabled: !!id,
    });
    const { data: albums, isLoading: albumsLoading } = useQuery({
        queryKey: ['albums', id],
        queryFn: () => browseApi.albums(1, 100, id).then(r => r.data),
        enabled: !!id,
    });
    const { data: trackPage, isLoading: tracksLoading } = useQuery({
        queryKey: ['artist-tracks', id],
        queryFn: () => browseApi.tracks(1, 200, 'title', 'asc', '', id!).then(r => r.data),
        enabled: !!id,
    });
    const tracks = useMemo(() => (trackPage?.items ?? []).map(toTrack), [trackPage]);

    if (artistLoading) return <PageLoading />;
    if (isError) return <QueryError error={error} retry={refetch} />;
    if (!artist) return <Typography>Artist not found</Typography>;

    const albumList = albums?.items ?? [];

    return (
        <Box>
            <IconButton aria-label="Back" onClick={() => navigate(-1)} sx={{ mb: 2 }}><ArrowBack /></IconButton>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3} sx={{ mb: 4 }} alignItems={{ xs: 'flex-start', sm: 'center' }}>
                <Cover artworkId={artist.artworkId} kind="artist" round size={320} sx={{ width: { xs: 120, sm: 160 } }} />
                <Box sx={{ minWidth: 0, flex: 1 }}>
                    <Typography variant="caption" color="text.secondary" textTransform="uppercase" letterSpacing={1}>Artist</Typography>
                    <Typography variant="h4" component="h1" fontWeight={700} sx={{ overflowWrap: 'anywhere' }}>{artist.name}</Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                        {artist.albumCount} {artist.albumCount === 1 ? 'album' : 'albums'} · {artist.trackCount} {artist.trackCount === 1 ? 'track' : 'tracks'}
                    </Typography>
                    <PlayActions tracks={tracks} />
                </Box>
            </Stack>

            {albumList.length > 0 && (
                <Box component="section" sx={{ mb: 4 }}>
                    <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
                        Albums <Chip size="small" label={albumList.length} sx={{ ml: 1, height: 20 }} />
                    </Typography>
                    <AlbumGrid albums={albumList}
                        caption={a => [a.year, a.trackCount != null ? `${a.trackCount} tracks` : null].filter(Boolean).join(' · ')} />
                </Box>
            )}

            {tracks.length > 0 && (
                <Box component="section">
                    <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
                        Tracks <Chip size="small" label={trackPage?.totalCount ?? 0} sx={{ ml: 1, height: 20 }} />
                    </Typography>
                    <Card><TrackList tracks={tracks} showCover /></Card>
                </Box>
            )}

            {albumList.length === 0 && tracks.length === 0 && !albumsLoading && !tracksLoading && (
                <Typography color="text.secondary" textAlign="center" sx={{ py: 4 }}>
                    No albums or tracks found for this artist.
                </Typography>
            )}
        </Box>
    );
};

export default ArtistDetailPage;
