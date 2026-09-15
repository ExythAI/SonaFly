import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import axios from 'axios';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Snackbar } from '@mui/material';

export function errorMessage(error: unknown): string {
    if (axios.isAxiosError(error)) {
        const data = error.response?.data;
        if (typeof data?.detail === 'string') return data.detail;
        if (Array.isArray(data?.errors)) return data.errors.join(' ');
        if (data?.errors && typeof data.errors === 'object') return Object.values(data.errors).flat().join(' ');
        if (!error.response) return 'Unable to reach the server. Check your connection and try again.';
        if (error.response.status === 403) return 'You do not have permission to do that.';
    }
    return error instanceof Error ? error.message : 'Something went wrong. Please try again.';
}

type Notice = { message: string; severity: 'success' | 'error' | 'info' };
const events = new EventTarget();
export const notify = (message: string, severity: Notice['severity'] = 'success') =>
    events.dispatchEvent(new CustomEvent('notice', { detail: { message, severity } }));
type Confirmation = { title: string; description: string; action?: string };
const ConfirmContext = createContext<(options: Confirmation) => Promise<boolean>>(() => Promise.resolve(false));
export const useConfirm = () => useContext(ConfirmContext);

export function FeedbackProvider({ children }: { children: ReactNode }) {
    const [notices, setNotices] = useState<Notice[]>([]);
    const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
    const resolver = useRef<((value: boolean) => void) | null>(null);
    useEffect(() => {
        const listener = (e: Event) => setNotices(list => [...list, (e as CustomEvent<Notice>).detail]);
        events.addEventListener('notice', listener);
        return () => events.removeEventListener('notice', listener);
    }, []);
    const confirm = useCallback((options: Confirmation) => new Promise<boolean>(resolve => {
        resolver.current?.(false);
        resolver.current = resolve;
        setConfirmation(options);
    }), []);
    const finish = (value: boolean) => {
        resolver.current?.(value);
        resolver.current = null;
        setConfirmation(null);
    };
    const closeNotice = () => setNotices(list => list.slice(1));
    const notice = notices[0];
    return <ConfirmContext.Provider value={confirm}>
        {children}
        <Snackbar key={notice?.message} open={!!notice} autoHideDuration={notice?.severity === 'error' ? 10000 : 4500}
            onClose={(_, reason) => { if (reason !== 'clickaway') closeNotice(); }} anchorOrigin={{ vertical: 'top', horizontal: 'right' }}>
            <Alert severity={notice?.severity} onClose={closeNotice} variant="filled" sx={{ maxWidth: 440 }}>{notice?.message}</Alert>
        </Snackbar>
        <Dialog open={!!confirmation} onClose={() => finish(false)} maxWidth="xs" fullWidth aria-labelledby="confirm-title">
            <DialogTitle id="confirm-title">{confirmation?.title}</DialogTitle>
            <DialogContent><DialogContentText>{confirmation?.description}</DialogContentText></DialogContent>
            <DialogActions><Button autoFocus onClick={() => finish(false)}>Cancel</Button>
                <Button color="error" variant="contained" onClick={() => finish(true)}>{confirmation?.action ?? 'Delete'}</Button>
            </DialogActions>
        </Dialog>
    </ConfirmContext.Provider>;
}
