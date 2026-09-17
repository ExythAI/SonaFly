import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Alert, Box, Typography, Card, CardContent, Button, TextField, Dialog, DialogTitle, DialogContent, DialogActions, IconButton, Chip, Stack, Menu, MenuItem } from '@mui/material';
import { Add, Delete, FolderOpen, Refresh, MoreVert } from '@mui/icons-material';
import { libraryRootsApi, identificationApi, settingsApi } from '../api/client';
import { useConfirm, errorMessage } from '../components/Feedback';
import { PageHeader, PageLoading, QueryError, EmptyState } from '../components/PageParts';

type Folder = { id: string; name: string; path: string; isEnabled: boolean; lastScanStatus?: string; lastScanCompletedUtc?: string; lastScanError?: string };

function IdentificationCard() {
    const qc = useQueryClient();
    const confirm = useConfirm();
    const status = useQuery({ queryKey: ['identification-status'], queryFn: () => identificationApi.status().then(r => r.data) });
    const [keyInput, setKeyInput] = useState('');
    const refresh = () => { qc.invalidateQueries({ queryKey: ['identification-status'] }); };
    const saveKey = useMutation({
        mutationFn: () => settingsApi.saveAcoustIdKey(keyInput.trim()),
        meta: { successMessage: 'AcoustID key saved. Online lookups are now available.' },
        onSuccess: () => { setKeyInput(''); refresh(); },
    });
    const clearKey = useMutation({
        mutationFn: () => settingsApi.clearAcoustIdKey(),
        meta: { successMessage: 'Stored key cleared. The configuration file value applies again.' },
        onSuccess: refresh,
    });
    const clearStoredKey = async () => {
        if (await confirm({ title: 'Clear the stored AcoustID key?', description: 'The server will fall back to the key from its configuration file, if one is set. Online lookups stop working if neither is configured.', action: 'Clear key' })) clearKey.mutate();
    };
    const data = status.data;
    return <Card sx={{ mb: 2 }}><CardContent>
        <Typography variant="h6">Music identification</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Analyse tracks against AcoustID and MusicBrainz. The key is stored encrypted on the server and never displayed again.
        </Typography>
        {status.isLoading ? <PageLoading /> : status.isError ? <QueryError error={status.error} retry={status.refetch} /> : data && <>
            <Stack direction="row" gap={1} sx={{ mb: 1, flexWrap: 'wrap' }}>
                <Chip size="small" label={data.enabled ? 'Enabled' : 'Disabled'} color={data.enabled ? 'success' : 'default'} />
                <Chip size="small" label={data.acoustIdConfigured ? `AcoustID key set (${data.acoustIdSource})` : 'No AcoustID key'} color={data.acoustIdConfigured ? 'success' : 'warning'} />
                {data.localOnlyMode && <Chip size="small" label="Local-only mode" color="warning" />}
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{data.message}</Typography>
            {data.blockers.map(b => <Alert key={b} severity="warning" sx={{ mt: 1 }}>{b}</Alert>)}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }} sx={{ mt: 2 }}>
                <TextField fullWidth type="password" autoComplete="new-password" label="AcoustID client key"
                    value={keyInput} onChange={e => { saveKey.reset(); setKeyInput(e.target.value); }}
                    placeholder={data.acoustIdConfigured ? 'Key is set — enter a new one to replace it' : 'Paste the key from acoustid.org'}
                    helperText="Saved encrypted. To use a configuration-file key instead, clear the stored one." />
                <Stack direction="row" gap={1} sx={{ whiteSpace: 'nowrap' }}>
                    <Button variant="contained" disabled={saveKey.isPending || !keyInput.trim()}
                        onClick={() => saveKey.mutate()}>{saveKey.isPending ? 'Saving…' : 'Save key'}</Button>
                    <Button variant="outlined" color="warning" disabled={clearKey.isPending || !data.acoustIdConfigured || data.acoustIdSource !== 'Database'}
                        onClick={() => void clearStoredKey()}>Clear</Button>
                </Stack>
            </Stack>
            {saveKey.isError && <Alert severity="error" sx={{ mt: 2 }}>{errorMessage(saveKey.error)}</Alert>}
        </>}
    </CardContent></Card>;
}
export default function LibraryRootsPage() {
    const qc = useQueryClient();
    const confirm = useConfirm();
    const roots = useQuery({ queryKey: ['library-roots'], queryFn: () => libraryRootsApi.getAll().then(r => r.data as Folder[]), refetchInterval: 3000 });
    const [open, setOpen] = useState(false);
    const [form, setForm] = useState({ name: '', path: '' });
    const [menu, setMenu] = useState<{ anchor: HTMLElement; folder: Folder } | null>(null);
    const refresh = () => { qc.invalidateQueries({ queryKey: ['library-roots'] }); qc.invalidateQueries({ queryKey: ['system-status'] }); };
    const create = useMutation({ mutationFn: () => libraryRootsApi.create({ name: form.name.trim(), path: form.path.trim() }), meta: { successMessage: 'Music folder added. Start a scan to import its music.' }, onSuccess: () => { refresh(); setOpen(false); setForm({ name: '', path: '' }); } });
    const remove = useMutation({ mutationFn: (id: string) => libraryRootsApi.delete(id), meta: { successMessage: 'Music folder removed.' }, onSuccess: refresh });
    const scan = useMutation({ mutationFn: ({ id, full }: { id: string; full: boolean }) => libraryRootsApi.triggerScan(id, full), meta: { successMessage: 'Scan queued. Progress will update automatically.' }, onSuccess: refresh });
    const removeFolder = async (folder: Folder) => {
        setMenu(null);
        if (await confirm({ title: `Remove “${folder.name}”?`, description: 'This removes the folder and its indexed tracks from SonaFly. Your original music files stay on disk.', action: 'Remove folder' })) remove.mutate(folder.id);
    };
    return <Box>
        <PageHeader title="Music folders" subtitle="Choose where SonaFly finds your music. Your original files stay on disk."
            action={<Button variant="contained" startIcon={<Add />} onClick={() => { create.reset(); setOpen(true); }}>Add music folder</Button>} />
        <IdentificationCard />
        {roots.isLoading ? <PageLoading /> : roots.isError ? <QueryError error={roots.error} retry={roots.refetch} /> : roots.data?.length === 0 ?
            <EmptyState title="Your collection starts here" description="Add a folder that this server can access, then scan it to discover albums and tracks." action={<Button onClick={() => setOpen(true)} startIcon={<Add />}>Add music folder</Button>} /> :
            <Stack spacing={2}>{roots.data?.map(folder => <Card key={folder.id}><CardContent>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }}>
                    <FolderOpen sx={{ color: 'primary.light', fontSize: 32 }} /><Box flex={1} minWidth={0}>
                        <Stack direction="row" alignItems="center" gap={1}><Typography variant="h6">{folder.name}</Typography>{!folder.isEnabled && <Chip size="small" label="Disabled" />}</Stack>
                        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{folder.path}</Typography>
                        <Typography variant="caption" color="text.secondary">{folder.lastScanCompletedUtc ? `Last scanned ${new Date(folder.lastScanCompletedUtc).toLocaleString()}` : 'Not scanned yet'}</Typography>
                    </Box>
                    <Stack direction="row" alignItems="center" gap={1}>
                        {folder.lastScanStatus && <Chip size="small" label={folder.lastScanStatus} color={folder.lastScanStatus === 'Failed' ? 'error' : folder.lastScanStatus === 'Partial' ? 'warning' : folder.lastScanStatus === 'Running' ? 'warning' : 'default'} />}
                        <Button startIcon={<Refresh />} variant="outlined" disabled={!folder.isEnabled || scan.isPending || ['Running', 'Queued'].includes(folder.lastScanStatus ?? '')}
                            onClick={() => scan.mutate({ id: folder.id, full: false })}>Scan</Button>
                        <IconButton aria-label={`More actions for ${folder.name}`} onClick={e => setMenu({ anchor: e.currentTarget, folder })}><MoreVert /></IconButton>
                    </Stack>
                </Stack>
                {folder.lastScanError && <Alert severity="error" sx={{ mt: 2 }}>{folder.lastScanError}</Alert>}
            </CardContent></Card>)}</Stack>}
        <Menu anchorEl={menu?.anchor} open={!!menu} onClose={() => setMenu(null)}>
            <MenuItem disabled={scan.isPending || !menu?.folder.isEnabled || ['Running', 'Queued'].includes(menu?.folder.lastScanStatus ?? '')} onClick={() => { if (menu) scan.mutate({ id: menu.folder.id, full: true }); setMenu(null); }}>Rescan all metadata</MenuItem>
            <MenuItem disabled={remove.isPending} onClick={() => { if (menu) void removeFolder(menu.folder); }} sx={{ color: 'error.main' }}><Delete fontSize="small" sx={{ mr: 1 }} />Remove folder</MenuItem>
        </Menu>
        <Dialog open={open} onClose={() => { if (!create.isPending) setOpen(false); }} maxWidth="sm" fullWidth>
            <DialogTitle>Add music folder</DialogTitle>
            <Box component="form" onSubmit={e => { e.preventDefault(); create.mutate(); }}>
                <DialogContent><Stack spacing={2}>
                    {create.isError && <Alert severity="error">{errorMessage(create.error)}</Alert>}
                    <TextField autoFocus required label="Folder name" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="My music" />
                    <TextField required label="Folder path on the server" value={form.path} onChange={e => setForm({ ...form, path: e.target.value })} placeholder="/music/library-main" helperText="For Docker, use the path inside the container, such as /music/library-main." />
                </Stack></DialogContent>
                <DialogActions><Button disabled={create.isPending} onClick={() => setOpen(false)}>Cancel</Button><Button type="submit" variant="contained" disabled={create.isPending || !form.name.trim() || !form.path.trim()}>{create.isPending ? 'Adding…' : 'Add folder'}</Button></DialogActions>
            </Box>
        </Dialog>
    </Box>;
}
