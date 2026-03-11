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

interface Restriction {
    id: string;
    userId: string;
    restrictionType: string;
    targetId: string;
    targetName: string | null;
}

interface User {
    id: string;
    userName: string;
    displayName: string;
}

const RestrictionsPage: React.FC = () => {
    const qc = useQueryClient();
    const [selectedUser, setSelectedUser] = useState<User | null>(null);
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
        restrictions?.some((r: Restriction) => r.targetId === targetId) ?? false;

    const handleQuickRestrict = (targetId: string) => {
        if (!selectedUser) return;
        if (isRestricted(targetId)) {
            const r = restrictions?.find((r: Restriction) => r.targetId === targetId);
            if (r) removeMut.mutate(r.id);
        } else {
            addMut.mutate({ userId: selectedUser.id, type: addType, targetId });
        }
    };

    const typeColor = (type: string) =>
        type === 'Album' ? 'primary' : type === 'Artist' ? 'secondary' : 'warning';

    // Get filtered items for the current type
    const getItems = () => {
        const term = filterText.toLowerCase();
        if (addType === 'Album') {
            const items = albumsData?.items || [];
            return term ? items.filter((a: any) => a.title.toLowerCase().includes(term) || (a.artistName || '').toLowerCase().includes(term)) : items;
        } else if (addType === 'Artist') {
            const items = artistsData?.items || [];
            return term ? items.filter((a: any) => a.name.toLowerCase().includes(term)) : items;
        } else {
            const items = genresData || [];
            return term ? items.filter((g: any) => g.name.toLowerCase().includes(term)) : items;
        }
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
                    {users?.filter((u: User) => u.userName !== 'admin').map((u: User) => (
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
                            Add Restriction
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
                                {restrictions?.map((r: Restriction) => (
                                    <TableRow key={r.id}>
                                        <TableCell>
                                            <Chip size="small" label={r.restrictionType} color={typeColor(r.restrictionType) as any} sx={{ height: 22 }} />
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

            {/* Add Restriction Dialog */}
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
                        onChange={e => setAddType(e.target.value as any)}
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
                            {getItems().map((item: any) => {
                                const id = item.id;
                                const restricted = isRestricted(id);
                                const name = addType === 'Album' ? item.title : item.name;
                                const subtitle = addType === 'Album' ? (item.artistName || 'Unknown Artist') :
                                                 addType === 'Artist' ? `${item.albumCount ?? 0} albums` :
                                                 `${item.trackCount ?? 0} tracks`;
                                const avatar = addType === 'Album' ? artworkUrl(item.artworkId) :
                                               addType === 'Artist' ? artworkUrl(item.artworkId) : undefined;

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
