import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
    Box, Typography, Card, Button, TextField, Dialog, DialogTitle,
    DialogContent, DialogActions, Table, TableHead, TableRow, TableCell, TableBody,
    IconButton, Chip, CircularProgress, MenuItem
} from '@mui/material';
import { Add, Delete, Block, CheckCircle, Key } from '@mui/icons-material';
import { usersApi } from '../api/client';

const UsersPage: React.FC = () => {
    const qc = useQueryClient();
    const { data: users, isLoading } = useQuery({ queryKey: ['users'], queryFn: () => usersApi.getAll().then(r => r.data) });
    const [open, setOpen] = useState(false);
    const [pwDialog, setPwDialog] = useState<string | null>(null);
    const [newPw, setNewPw] = useState('');
    const [form, setForm] = useState({ userName: '', email: '', displayName: '', password: '', role: 'User' });

    const createMut = useMutation({ mutationFn: () => usersApi.create(form), onSuccess: () => { qc.invalidateQueries({ queryKey: ['users'] }); setOpen(false); } });
    const deleteMut = useMutation({ mutationFn: (id: string) => usersApi.delete(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }) });
    const disableMut = useMutation({ mutationFn: (id: string) => usersApi.disable(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }) });
    const enableMut = useMutation({ mutationFn: (id: string) => usersApi.enable(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }) });
    const resetPwMut = useMutation({ mutationFn: ({ id, pw }: { id: string; pw: string }) => usersApi.resetPassword(id, pw), onSuccess: () => setPwDialog(null) });

    if (isLoading) return <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>;

    return (
        <Box>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
                <Box><Typography variant="h4">Users</Typography><Typography variant="body2" color="text.secondary">Manage user accounts</Typography></Box>
                <Button variant="contained" startIcon={<Add />} onClick={() => setOpen(true)}>Add User</Button>
            </Box>
            <Card>
                <Table>
                    <TableHead><TableRow>
                        <TableCell>Username</TableCell><TableCell>Display Name</TableCell><TableCell>Email</TableCell>
                        <TableCell>Role</TableCell><TableCell>Status</TableCell><TableCell align="right">Actions</TableCell>
                    </TableRow></TableHead>
                    <TableBody>
                        {users?.map((u: any) => (
                            <TableRow key={u.id}>
                                <TableCell><Typography fontWeight={600}>{u.userName}</Typography></TableCell>
                                <TableCell>{u.displayName}</TableCell>
                                <TableCell><Typography variant="body2" color="text.secondary">{u.email}</Typography></TableCell>
                                <TableCell><Chip size="small" label={u.roles?.[0] ?? 'User'} sx={{ height: 22 }} /></TableCell>
                                <TableCell><Chip size="small" label={u.isEnabled ? 'Active' : 'Disabled'} color={u.isEnabled ? 'success' : 'error'} sx={{ height: 22 }} /></TableCell>
                                <TableCell align="right">
                                    <IconButton size="small" title="Reset Password" onClick={() => setPwDialog(u.id)}><Key fontSize="small" /></IconButton>
                                    {u.isEnabled
                                        ? <IconButton size="small" title="Disable" onClick={() => disableMut.mutate(u.id)}><Block fontSize="small" /></IconButton>
                                        : <IconButton size="small" title="Enable" onClick={() => enableMut.mutate(u.id)} color="success"><CheckCircle fontSize="small" /></IconButton>}
                                    <IconButton size="small" title="Delete" onClick={() => deleteMut.mutate(u.id)} color="error"><Delete fontSize="small" /></IconButton>
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </Card>

            <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>Create User</DialogTitle>
                <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
                    <TextField label="Username" value={form.userName} onChange={e => setForm({ ...form, userName: e.target.value })} />
                    <TextField label="Email" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} />
                    <TextField label="Display Name" value={form.displayName} onChange={e => setForm({ ...form, displayName: e.target.value })} />
                    <TextField label="Password" type="password" value={form.password} onChange={e => setForm({ ...form, password: e.target.value })} />
                    <TextField label="Role" select value={form.role} onChange={e => setForm({ ...form, role: e.target.value })}>
                        <MenuItem value="Admin">Admin</MenuItem><MenuItem value="User">User</MenuItem>
                    </TextField>
                </DialogContent>
                <DialogActions><Button onClick={() => setOpen(false)}>Cancel</Button><Button variant="contained" onClick={() => createMut.mutate()}>Create</Button></DialogActions>
            </Dialog>

            <Dialog open={!!pwDialog} onClose={() => setPwDialog(null)} maxWidth="xs" fullWidth>
                <DialogTitle>Reset Password</DialogTitle>
                <DialogContent sx={{ pt: '16px !important' }}><TextField fullWidth label="New Password" type="password" value={newPw} onChange={e => setNewPw(e.target.value)} /></DialogContent>
                <DialogActions><Button onClick={() => setPwDialog(null)}>Cancel</Button><Button variant="contained" onClick={() => { if (pwDialog) resetPwMut.mutate({ id: pwDialog, pw: newPw }); }}>Reset</Button></DialogActions>
            </Dialog>
        </Box>
    );
};

export default UsersPage;
