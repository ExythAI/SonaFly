import { Alert } from '@mui/material';
import { useConfirm, errorMessage } from '../components/Feedback';
import { QueryError } from '../components/PageParts';
import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, Button, TextField, Dialog, DialogTitle,
    DialogContent, DialogActions, Table, TableHead, TableRow, TableCell, TableBody,
    IconButton, Chip, CircularProgress, MenuItem
} from '@mui/material';
import { Add, Delete, Block, CheckCircle, Key } from '@mui/icons-material';
import { usersApi } from '../api/client';
import { useAuth } from '../auth/AuthContext';

const UsersPage: React.FC = () => {
    const qc = useQueryClient();
    const confirm = useConfirm();
    const { user: currentUser } = useAuth();
    const { data: users, isLoading, isError, error, refetch } = useQuery({ queryKey: ['users'], queryFn: () => usersApi.getAll().then(r => r.data) });
    const [open, setOpen] = useState(false);
    const [pwDialog, setPwDialog] = useState<string | null>(null);
    const [newPw, setNewPw] = useState('');
    const [form, setForm] = useState({ userName: '', email: '', displayName: '', password: '', role: 'User' });

    const createMut = useMutation({ mutationFn: () => usersApi.create(form), onSuccess: () => { qc.invalidateQueries({ queryKey: ['users'] }); setOpen(false); setForm({ userName: '', email: '', displayName: '', password: '', role: 'User' }); } });
    const deleteMut = useMutation({ mutationFn: (id: string) => usersApi.delete(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }) });
    const disableMut = useMutation({ mutationFn: (id: string) => usersApi.disable(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }) });
    const enableMut = useMutation({ mutationFn: (id: string) => usersApi.enable(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }) });
    const resetPwMut = useMutation({ mutationFn: ({ id, pw }: { id: string; pw: string }) => usersApi.resetPassword(id, pw), onSuccess: () => setPwDialog(null) });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    if (isError) return <QueryError error={error} retry={refetch} />;

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2, mb: 3 }}>
                <Box><Typography variant="h4">Users</Typography><Typography variant="body2" color="text.secondary">Manage user accounts</Typography></Box>
                <Button variant="contained" startIcon={<Add />} onClick={() => { createMut.reset(); setOpen(true); }}>Add User</Button>
            </Box>
            <Card sx={{ overflowX: 'auto' }}>
                <Table>
                    <TableHead><TableRow>
                        <TableCell>Username</TableCell><TableCell>Display Name</TableCell><TableCell>Email</TableCell>
                        <TableCell>Role</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                    </TableRow></TableHead>
                    <TableBody>
                        {users?.map((u) => (
                            <TableRow key={u.id}>
                                <TableCell><Typography fontWeight={600}>{u.userName}</Typography></TableCell>
                                <TableCell>{u.displayName}</TableCell>
                                <TableCell><Typography variant="body2" color="text.secondary">{u.email}</Typography></TableCell>
                                <TableCell><Chip size="small" label={u.roles?.[0] ?? 'User'} sx={{ height: 22 }} /></TableCell>
                                <TableCell><Chip size="small" label={u.isEnabled ? 'Active' : 'Disabled'} color={u.isEnabled ? 'success' : 'error'} sx={{ height: 22 }} /></TableCell>
                                <TableCell align="right">
                                    <IconButton size="small" title="Reset Password" onClick={() => { setNewPw(''); resetPwMut.reset(); setPwDialog(u.id); }}><Key fontSize="small" /></IconButton>
                                    {u.isEnabled
                                        ? <IconButton size="small" title={u.id === currentUser?.id ? 'You cannot disable your own account' : 'Disable'} disabled={disableMut.isPending || u.id === currentUser?.id} onClick={() => disableMut.mutate(u.id)}><Block fontSize="small" /></IconButton>
                                        : <IconButton size="small" title="Enable" disabled={enableMut.isPending} onClick={() => enableMut.mutate(u.id)} color="success"><CheckCircle fontSize="small" /></IconButton>}
                                    <IconButton size="small" title={u.id === currentUser?.id ? 'You cannot delete your own account' : 'Delete'} disabled={deleteMut.isPending || u.id === currentUser?.id} onClick={async () => { if (await confirm({ title: `Delete “${u.userName}”?`, description: "This permanently deletes this account and its owned playlists. This cannot be undone." })) deleteMut.mutate(u.id); }} color="error"><Delete fontSize="small" /></IconButton>
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </Card>

            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>Create User</DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
                    {createMut.isError && <Alert severity="error">{errorMessage(createMut.error)}</Alert>}
                    <TextField required label="Username" value={form.userName} onChange={e => setForm({ ...form, userName: e.target.value })} />
                    <TextField type="email" required label="Email" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} />
                    <TextField label="Display Name" value={form.displayName} onChange={e => setForm({ ...form, displayName: e.target.value })} />
                    <TextField required helperText="At least 6 characters, including a lowercase letter and a number." label="Password" type="password" value={form.password} onChange={e => setForm({ ...form, password: e.target.value })} />
                    <TextField label="Role" select value={form.role} onChange={e => setForm({ ...form, role: e.target.value })}>
                        <MenuItem value="Admin">Admin</MenuItem><MenuItem value="User">User</MenuItem>
                    </TextField>
                </DialogContent>
                <DialogActions><Button onClick={() => setOpen(false)}>Cancel</Button><Button variant="contained" disabled={createMut.isPending || !form.userName.trim() || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email) || !/^(?=.*[a-z])(?=.*\d).{6,}$/.test(form.password)} onClick={() => createMut.mutate()}>{createMut.isPending ? 'Creating…' : 'Create'}</Button></DialogActions>
            </Dialog>

            <Dialog open={!!pwDialog} onClose={() => setPwDialog(null)} maxWidth="xs" fullWidth>
                <DialogTitle>Reset Password</DialogTitle>
                <DialogContent sx={{ pt: '16px !important' }}>{resetPwMut.isError && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(resetPwMut.error)}</Alert>}<TextField fullWidth helperText="At least 6 characters, including a lowercase letter and a number." label="New Password" type="password" value={newPw} onChange={e => setNewPw(e.target.value)} /></DialogContent>
                <DialogActions><Button onClick={() => setPwDialog(null)}>Cancel</Button><Button variant="contained" disabled={resetPwMut.isPending || !/^(?=.*[a-z])(?=.*\d).{6,}$/.test(newPw)} onClick={() => { if (pwDialog) resetPwMut.mutate({ id: pwDialog, pw: newPw }); }}>{resetPwMut.isPending ? 'Resetting…' : 'Reset password'}</Button></DialogActions>
            </Dialog>
        </Box>
    );
};

export default UsersPage;
