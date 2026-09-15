import React, { useState } from 'react';
import {
    Box, Card, TextField, Typography, Button, CircularProgress, Alert
} from '@mui/material';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../components/Feedback';

/**
 * Shown instead of the app when the account still holds a bootstrap or
 * administrator-assigned password. The server rejects every other endpoint with
 * `403 password_change_required` until this succeeds, so there is no way past it.
 */
const ChangePasswordPage: React.FC = () => {
    const { changePassword, logout } = useAuth();
    const [currentPassword, setCurrentPassword] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');

        if (newPassword !== confirmPassword) {
            setError('The new passwords do not match.');
            return;
        }
        if (newPassword === currentPassword) {
            setError('Choose a password different from your current one.');
            return;
        }

        setLoading(true);
        try {
            await changePassword(currentPassword, newPassword);
        } catch (err) {
            setError(errorMessage(err));
        } finally {
            setLoading(false);
        }
    };

    return (
        <Box sx={{
            minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center',
            bgcolor: 'background.default',
            background: 'radial-gradient(ellipse at 50% 0%, rgba(124,77,255,0.15) 0%, transparent 60%)',
        }}>
            <Card sx={{ p: { xs: 3, sm: 5 }, width: 420, maxWidth: 'calc(100% - 32px)', textAlign: 'center' }}>
                <Typography variant="h5" component="h1" mb={1}>Choose a new password</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                    This account was created with a temporary password. Replace it to continue.
                </Typography>
                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
                <form onSubmit={handleSubmit}>
                    <TextField fullWidth autoComplete="current-password" required label="Temporary password"
                        type="password" value={currentPassword} autoFocus
                        onChange={e => setCurrentPassword(e.target.value)} sx={{ mb: 2 }} />
                    <TextField fullWidth autoComplete="new-password" required label="New password"
                        type="password" value={newPassword}
                        onChange={e => setNewPassword(e.target.value)} sx={{ mb: 2 }} />
                    <TextField fullWidth autoComplete="new-password" required label="Confirm new password"
                        type="password" value={confirmPassword}
                        onChange={e => setConfirmPassword(e.target.value)} sx={{ mb: 3 }} />
                    <Button fullWidth variant="contained" size="large" type="submit" disabled={loading}
                        sx={{ py: 1.5, fontSize: 16 }}>
                        {loading ? <CircularProgress size={24} /> : 'Change Password'}
                    </Button>
                </form>
                <Button fullWidth sx={{ mt: 1 }} onClick={() => { void logout(); }}>
                    Sign out
                </Button>
            </Card>
        </Box>
    );
};

export default ChangePasswordPage;
