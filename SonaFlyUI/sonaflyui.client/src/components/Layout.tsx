import React, { useState } from 'react';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import {
    Box, Drawer, AppBar, Toolbar, Typography, List, ListItemButton,
    ListItemIcon, ListItemText, IconButton, Divider, Avatar, Chip
} from '@mui/material';
import {
    Dashboard, LibraryMusic, FolderOpen, People, QueueMusic,
    Search, Settings, Menu as MenuIcon, Logout, Album, MusicNote, Block as BlockIcon, MeetingRoom
} from '@mui/icons-material';
import { useAuth } from '../auth/AuthContext';
import { NowPlayingBar, PLAYER_HEIGHT, MINI_PLAYER_HEIGHT } from './NowPlayingBar';
import { usePlayer } from './PlayerContext';

const drawerWidth = 260;

const navItems = [
    { label: 'Dashboard', icon: <Dashboard />, path: '/' },
    { label: 'Library Roots', icon: <FolderOpen />, path: '/library-roots', admin: true },
    { label: 'Artists', icon: <LibraryMusic />, path: '/artists' },
    { label: 'Albums', icon: <Album />, path: '/albums' },
    { label: 'Tracks', icon: <MusicNote />, path: '/tracks' },
    { label: 'Search', icon: <Search />, path: '/search' },
    { label: 'Playlists', icon: <QueueMusic />, path: '/playlists' },
    { label: 'Mixed Tapes', icon: <Album />, path: '/mixed-tapes' },
    { label: 'Users', icon: <People />, path: '/users', admin: true },
    { label: 'Restrictions', icon: <BlockIcon />, path: '/restrictions', admin: true },
    { label: 'Auditoriums', icon: <MeetingRoom />, path: '/auditoriums', admin: true },
    { label: 'System', icon: <Settings />, path: '/system' },
];

const Layout: React.FC = () => {
    const { user, logout, isAdmin } = useAuth();
    const { currentTrack } = usePlayer();
    const navigate = useNavigate();
    const location = useLocation();
    const [mobileOpen, setMobileOpen] = useState(false);

    const filteredNav = navItems.filter(item => !item.admin || isAdmin);

    const drawer = (
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
            <Toolbar sx={{ px: 2.5 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                    <Box sx={{
                        width: 36, height: 36, borderRadius: '10px',
                        background: 'linear-gradient(135deg, #7C4DFF 0%, #00E5FF 100%)',
                        display: 'flex', alignItems: 'center', justifyContent: 'center'
                    }}>
                        <MusicNote sx={{ color: '#fff', fontSize: 20 }} />
                    </Box>
                    <Typography variant="h6" sx={{
                        fontWeight: 800,
                        background: 'linear-gradient(135deg, #B388FF, #00E5FF)',
                        WebkitBackgroundClip: 'text',
                        WebkitTextFillColor: 'transparent'
                    }}>
                        SonaFly
                    </Typography>
                </Box>
            </Toolbar>
            <Divider />
            <List sx={{ flex: 1, px: 1.5, py: 1 }}>
                {filteredNav.map((item) => (
                    <ListItemButton
                        key={item.path}
                        selected={location.pathname === item.path}
                        onClick={() => { navigate(item.path); setMobileOpen(false); }}
                        sx={{
                            borderRadius: 2, mb: 0.5, py: 1,
                            '&.Mui-selected': {
                                bgcolor: 'rgba(124, 77, 255, 0.12)',
                                '&:hover': { bgcolor: 'rgba(124, 77, 255, 0.18)' },
                                '& .MuiListItemIcon-root': { color: 'primary.main' },
                                '& .MuiListItemText-primary': { color: 'primary.light', fontWeight: 600 }
                            }
                        }}
                    >
                        <ListItemIcon sx={{ minWidth: 40, color: 'text.secondary' }}>{item.icon}</ListItemIcon>
                        <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 14 }} />
                    </ListItemButton>
                ))}
            </List>
            <Divider />
            <Box sx={{ p: 2, display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Avatar sx={{ width: 32, height: 32, bgcolor: 'primary.dark', fontSize: 14 }}>
                    {user?.displayName?.[0] ?? 'U'}
                </Avatar>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography variant="body2" noWrap fontWeight={600}>{user?.displayName}</Typography>
                    <Chip label={user?.roles?.[0] ?? 'User'} size="small" sx={{ height: 18, fontSize: 10 }} />
                </Box>
                <IconButton size="small" onClick={logout} title="Logout"><Logout fontSize="small" /></IconButton>
            </Box>
        </Box>
    );

    return (
        <Box sx={{ display: 'flex', height: '100vh', bgcolor: 'background.default', overflow: 'hidden' }}>
            <AppBar position="fixed" sx={{
                display: { md: 'none' },
                bgcolor: 'background.paper',
                borderBottom: '1px solid rgba(255,255,255,0.06)'
            }}>
                <Toolbar>
                    <IconButton edge="start" onClick={() => setMobileOpen(!mobileOpen)}><MenuIcon /></IconButton>
                    <Typography variant="h6" sx={{ ml: 1 }}>SonaFly</Typography>
                </Toolbar>
            </AppBar>
            <Box component="nav" sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
                <Drawer variant="temporary" open={mobileOpen} onClose={() => setMobileOpen(false)}
                    sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: drawerWidth } }}>
                    {drawer}
                </Drawer>
                <Drawer variant="permanent"
                    sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: drawerWidth } }} open>
                    {drawer}
                </Drawer>
            </Box>
            {/* Right column: scrollable content + player footer */}
            <Box sx={{ display: 'flex', flexDirection: 'column', flexGrow: 1, minWidth: 0, height: '100vh' }}>
                <Box component="main" sx={{
                    flexGrow: 1, overflow: 'auto',
                    p: { xs: 2, md: 3 },
                    mt: { xs: 8, md: 0 },
                }}>
                    <Outlet />
                </Box>
                <NowPlayingBar />
            </Box>
        </Box>
    );
};

export default Layout;
