import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, Button, TextField, Dialog, DialogTitle,
    DialogContent, DialogActions, Table, TableHead, TableRow, TableCell, TableBody,
    IconButton, Chip, CircularProgress, MenuItem, Alert, Avatar, List,
    ListItemButton, ListItemAvatar, ListItemText, InputAdornment, Pagination
} from '@mui/material';
import { Delete, Add, Block as BlockIcon, Search } from '@mui/icons-material';
import { usersApi, restrictionsApi, browseApi, artworkUrl } from '../api/client';
// These used to be re-declared here with looser types (restrictionType as a bare
// string), which is how the `as any` casts crept in.
import type { RestrictionType, UserInfoDto, UserRestrictionDto } from '../api/types.ts';

const RestrictionsPage: React.FC = () => {
    const qc = useQueryClient();
    const [selectedUser, setSelectedUser] = useState<UserInfoDto | null>(null);
    const [addOpen, setAddOpen] = useState(false);
    const [addType, setAddType] = useState<'Album' | 'Artist' | 'Genre'>('Album');
    const [filterText, setFilterText] = useState('');
    const [page, setPage] = useState(1);
    const pageSize = 20;

    const { data: users, isLoading: usersLoading } = useQuery({
        queryKey: ['users'],
        queryFn: () => usersApi.getAll().then(r => r.data)
    });

    const { data: restrictions, isLoading: restrictionsLoading } = useQuery({
        queryKey: ['restrictions', selectedUser?.id],
        queryFn: () => selectedUser ? restrictionsApi.getForUser(selectedUser.id).then(r => r.data) : [],
        enabled: !!selectedUser,
    });

    // Load browsable lists for the Add dialog
    const { data: albumsData, isLoading: albumsLoading } = useQuery({
        queryKey: ['all-albums', page],
        queryFn: () => browseApi.albums(page, pageSize).then(r => r.data),
        enabled: addOpen && addType === 'Album',
    });

    const { data: artistsData, isLoading: artistsLoading } = useQuery({
        queryKey: ['all-artists', page],
        queryFn: () => browseApi.artists(page, pageSize).then(r => r.data),
        enabled: addOpen && addType === 'Artist',
    });

    const { data: genresData, isLoading: genresLoading } = useQuery({
        queryKey: ['all-genres'],
        queryFn: () => browseApi.genres().then(r => r.data),
        enabled: addOpen && addType === 'Genre',
    });

    // Reset page when type or dialog changes
    useEffect(() => { setPage(1); setFilterText(''); }, [addType, addOpen]);

    const addMut = useMutation({
        mutationFn: ({ userId, type, targetId }: { userId: string; type: string; targetId: string }) =>
            restrictionsApi.add(userId, type, targetId),
        onSuccess: () => {
            qc.invalidateQueries({ queryKey: ['restrictions', selectedUser?.id] });
        },
    });

    const removeMut = useMutation({
        mutationFn: (id: string) => restrictionsApi.remove(id),
        onSuccess: () => qc.invalidateQueries({ queryKey: ['restrictions', selectedUser?.id] }),
    });

    const isRestricted = (targetId: string) =>
        restrictions?.some((r: UserRestrictionDto) => r.targetId === targetId) ?? false;

    const handleQuickRestrict = (targetId: string) => {
        if (!selectedUser) return;
        if (isRestricted(targetId)) {
            const r = restrictions?.find((r: UserRestrictionDto) => r.targetId === targetId);
            if (r) removeMut.mutate(r.id);
        } else {
            addMut.mutate({ userId: selectedUser.id, type: addType, targetId });
        }
    };

    const typeColor = (type: RestrictionType) =>
        type === 'Album' ? 'primary' as const : type === 'Artist' ? 'secondary' as const : 'warning' as const;

    /**
     * Albums, artists and genres are three different shapes. Normalising them here means
     * the list below renders one thing, instead of picking fields off a union by checking
     * addType at every use — which is unreadable and, being unnarrowable, untypeable.
     */
    type Candidate = { id: string; name: string; subtitle: string; avatar?: string };

    const getItems = (): Candidate[] => {
        const term = filterText.toLowerCase();
        const matches = (...fields: (string | null | undefined)[]) =>
            !term || fields.some(f => (f ?? '').toLowerCase().includes(term));

        if (addType === 'Album') {
            return (albumsData?.items ?? [])
                .filter(a => matches(a.title, a.artistName))
                .map(a => ({ id: a.id, name: a.title,
                    subtitle: a.artistName || 'Unknown Artist', avatar: artworkUrl(a.artworkId) }));
        }
        if (addType === 'Artist') {
            return (artistsData?.items ?? [])
                .filter(a => matches(a.name))
                .map(a => ({ id: a.id, name: a.name,
                    subtitle: `${a.albumCount} albums`, avatar: artworkUrl(a.artworkId) }));
        }
        return (genresData ?? [])
            .filter(g => matches(g.name))
            .map(g => ({ id: g.id, name: g.name, subtitle: `${g.trackCount} tracks` }));
    };

    const totalPages = addType === 'Album' ? Math.ceil((albumsData?.totalCount || 0) / pageSize) :
                       addType === 'Artist' ? Math.ceil((artistsData?.totalCount || 0) / pageSize) : 1;

    const isListLoading = addType === 'Album' ? albumsLoading :
                          addType === 'Artist' ? artistsLoading : genresLoading;

    if (usersLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
                <Box>
                    <Typography variant="h4">Content Restrictions</Typography>
                    <Typography variant="body2" color="text.secondary">
                        Control which albums, artists, or genres each user can see
                    </Typography>
                </Box>
            </Box>

            {/* User Selector */}
            <Card sx={{ p: 3, mb: 3 }}>
                <Typography variant="subtitle2" sx={{ mb: 1.5 }}>Select User</Typography>
                <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                    {users?.filter((u: UserInfoDto) => u.userName !== 'admin').map((u: UserInfoDto) => (
                        <Chip
                            key={u.id}
                            label={u.displayName || u.userName}
                            onClick={() => setSelectedUser(u)}
                            variant={selectedUser?.id === u.id ? 'filled' : 'outlined'}
                            color={selectedUser?.id === u.id ? 'primary' : 'default'}
                            sx={{ fontWeight: selectedUser?.id === u.id ? 700 : 400 }}
                        />
                    ))}
                </Box>
            </Card>

            {/* Restrictions List */}
            {selectedUser && (
                <Card>
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', p: 2 }}>
                        <Typography variant="h6">
                            Restrictions for <strong>{selectedUser.displayName || selectedUser.userName}</strong>
                        </Typography>
                        <Button variant="contained" startIcon={<Add />} onClick={() => setAddOpen(true)} size="small">
                            Add UserRestrictionDto
                        </Button>
                    </Box>

                    {restrictionsLoading ? (
                        <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress size={24} /></Box>
                    ) : restrictions?.length === 0 ? (
                        <Alert severity="info" sx={{ m: 2 }}>
                            No restrictions — this user can see the entire library.
                        </Alert>
                    ) : (
                        <Table>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Type</TableCell>
                                    <TableCell>Name</TableCell>
                                    <TableCell align="right">Remove</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {restrictions?.map((r: UserRestrictionDto) => (
                                    <TableRow key={r.id}>
                                        <TableCell>
                                            <Chip size="small" label={r.restrictionType} color={typeColor(r.restrictionType)} sx={{ height: 22 }} />
                                        </TableCell>
                                        <TableCell>
                                            <Typography fontWeight={500}>{r.targetName || r.targetId}</Typography>
                                        </TableCell>
                                        <TableCell align="right">
                                            <IconButton size="small" color="error" onClick={() => removeMut.mutate(r.id)} title="Remove restriction">
                                                <Delete fontSize="small" />
                                            </IconButton>
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    )}
                </Card>
            )}

            {/* Add UserRestrictionDto Dialog */}
            <Dialog open={addOpen} onClose={() => setAddOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                        <BlockIcon color="error" /> Restrict Content for {selectedUser?.displayName || selectedUser?.userName}
                    </Box>
                </DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important', minHeight: 400 }}>
                    {/* Type selector */}
                    <TextField
                        label="Type" select value={addType} size="small"
                        onChange={e => setAddType(e.target.value as RestrictionType)}
                    >
                        <MenuItem value="Album">Albums</MenuItem>
                        <MenuItem value="Artist">Artists</MenuItem>
                        <MenuItem value="Genre">Genres</MenuItem>
                    </TextField>

                    {/* Filter */}
                    <TextField
                        size="small" placeholder={`Filter ${addType.toLowerCase()}s...`}
                        value={filterText} onChange={e => setFilterText(e.target.value)}
                        InputProps={{ startAdornment: <InputAdornment position="start"><Search fontSize="small" /></InputAdornment> }}
                    />

                    {/* Browsable list */}
                    {isListLoading ? (
                        <Box sx={{ py: 4, textAlign: 'center' }}><CircularProgress size={24} /></Box>
                    ) : (
                        <List dense sx={{ maxHeight: 350, overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                            {getItems().map(({ id, name, subtitle, avatar }) => {
                                const restricted = isRestricted(id);

                                return (
                                    <ListItemButton
                                        key={id}
                                        onClick={() => handleQuickRestrict(id)}
                                        sx={{
                                            bgcolor: restricted ? 'error.dark' : 'transparent',
                                            opacity: restricted ? 0.7 : 1,
                                            '&:hover': { bgcolor: restricted ? 'error.main' : 'action.hover' }
                                        }}
                                    >
                                        <ListItemAvatar>
                                            <Avatar src={avatar} variant="rounded" sx={{ width: 40, height: 40 }}>
                                                {name?.[0] || '?'}
                                            </Avatar>
                                        </ListItemAvatar>
                                        <ListItemText
                                            primary={<Typography variant="body2" fontWeight={600}>{name}</Typography>}
                                            secondary={subtitle}
                                        />
                                        {restricted ? (
                                            <Chip size="small" label="Blocked" color="error" sx={{ height: 22 }} />
                                        ) : (
                                            <Chip size="small" label="Visible" variant="outlined" sx={{ height: 22 }} />
                                        )}
                                    </ListItemButton>
                                );
                            })}
                            {getItems().length === 0 && (
                                <Typography variant="body2" color="text.secondary" sx={{ p: 2, textAlign: 'center' }}>
                                    No matches found
                                </Typography>
                            )}
                        </List>
                    )}

                    {/* Pagination for albums/artists */}
                    {totalPages > 1 && (
                        <Box sx={{ display: 'flex', justifyContent: 'center' }}>
                            <Pagination count={totalPages} page={page} onChange={(_, p) => setPage(p)} size="small" />
                        </Box>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setAddOpen(false)}>Done</Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default RestrictionsPage;
