import React, { useState } from 'react';
import {
    Box, Card, TextField, Typography, Button, CircularProgress, Alert
} from '@mui/material';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../components/Feedback';
import { useNavigate } from 'react-router-dom';

const LoginPage: React.FC = () => {
    const { login } = useAuth();
    const navigate = useNavigate();
    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setLoading(true);
        try {
            await login(username, password);
            navigate('/');
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
                <Box
                    component="img"
                    src="/sonafly.png"
                    alt="SonaFly"
                    sx={{ maxWidth: 96, width: '100%', height: 'auto', mx: 'auto', mb: 2, borderRadius: '16px' }}
                />
                <Typography variant="h4" component="h1" mb={1}>SonaFly</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                    Sign in to your music server
                </Typography>
                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
                <form onSubmit={handleSubmit}>
                    <TextField fullWidth autoComplete="username" required label="Username" value={username}
                        onChange={e => setUsername(e.target.value)} sx={{ mb: 2 }} autoFocus />
                    <TextField fullWidth autoComplete="current-password" required label="Password" type="password" value={password}
                        onChange={e => setPassword(e.target.value)} sx={{ mb: 3 }} />
                    <Button fullWidth variant="contained" size="large" type="submit" disabled={loading}
                        sx={{ py: 1.5, fontSize: 16 }}>
                        {loading ? <CircularProgress size={24} /> : 'Sign In'}
                    </Button>
                </form>
            </Card>
        </Box>
    );
};

export default LoginPage;
