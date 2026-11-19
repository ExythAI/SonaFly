import React, { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Box, Card } from '@mui/material';
import { listeningApi } from '../api/client';
import { PlayActions } from '../components/Cards';
import { TrackList, formatRunningTime, toTrack } from '../components/Music';
import { EmptyState, PageHeader, PageLoading, QueryError } from '../components/PageParts';

const FavoritesPage: React.FC = () => {
    const { data, isLoading, isError, error, refetch } = useQuery({
        queryKey: ['favorites'],
        queryFn: () => listeningApi.favorites().then(r => r.data),
    });
    const tracks = useMemo(() => (data ?? []).map(toTrack), [data]);

    if (isLoading) return <PageLoading />;
    if (isError) return <QueryError error={error} retry={refetch} />;

    return (
        <Box>
            <PageHeader title="Favourites"
                subtitle={tracks.length ? `${tracks.length} ${tracks.length === 1 ? 'track' : 'tracks'} · ${formatRunningTime(tracks)}` : 'The tracks you have marked with a heart.'}
                action={tracks.length > 0 ? <PlayActions tracks={tracks} /> : undefined} />
            {tracks.length === 0
                ? <EmptyState title="No favourites yet" description="Select the heart beside any track, or in the player, and it will be kept here." />
                : <Card><TrackList tracks={tracks} showCover /></Card>}
        </Box>
    );
};

export default FavoritesPage;
