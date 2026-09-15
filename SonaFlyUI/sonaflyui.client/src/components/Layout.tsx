import { useState } from 'react';
import { Outlet, NavLink, useLocation, useNavigate } from 'react-router-dom';
import { Box, Drawer, Toolbar, Typography, List, ListItemButton, ListItemIcon, ListItemText, IconButton, Divider, Avatar, Button, Stack } from '@mui/material';
import { Dashboard, FolderOpen, People, QueueMusic, Search, Settings, Menu, Logout, Album, MusicNote, Block, MeetingRoom, LibraryMusic } from '@mui/icons-material';
import { useAuth } from '../auth/AuthContext';
import { NowPlayingBar } from './NowPlayingBar';

const drawerWidth = 232;
const groups = [
    { label: 'Library', items: [
        { label: 'Overview', icon: <Dashboard />, path: '/' },
        { label: 'Artists', icon: <LibraryMusic />, path: '/artists' },
        { label: 'Albums', icon: <Album />, path: '/albums' },
        { label: 'Tracks', icon: <MusicNote />, path: '/tracks' },
    ] },
    { label: 'Your music', items: [
        { label: 'Playlists', icon: <QueueMusic />, path: '/playlists' },
        { label: 'Mixed tapes', icon: <Album />, path: '/mixed-tapes' },
        { label: 'Auditoriums', icon: <MeetingRoom />, path: '/auditoriums' },
    ] },
    { label: 'Administration', admin: true, items: [
        { label: 'Music folders', icon: <FolderOpen />, path: '/library-roots' },
        { label: 'Users', icon: <People />, path: '/users' },
        { label: 'Restrictions', icon: <Block />, path: '/restrictions' },
        { label: 'System', icon: <Settings />, path: '/system' },
    ] },
];
export default function Layout() {
    const { user, logout, isAdmin } = useAuth();
    const [mobileOpen, setMobileOpen] = useState(false);
    const location = useLocation();
    const navigate = useNavigate();
    const section = groups.flatMap(g => g.items).find(i => i.path !== '/' && location.pathname.startsWith(i.path))?.label ?? 'Overview';
    const drawer = <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
        <Toolbar sx={{ gap: 1.25, minHeight: '80px !important' }}>
            <Box component="img" src="/sonafly.png" alt="" sx={{ width: 40, height: 40, borderRadius: 2 }} />
            <Box><Typography variant="h6" fontWeight={800}>SonaFly</Typography><Typography variant="caption" color="text.secondary">Your music, together.</Typography></Box>
        </Toolbar>
        <Box sx={{ flex: 1, overflowY: 'auto', px: 1.5 }}>
            {groups.filter(g => !g.admin || isAdmin).map(group => <Box key={group.label} mb={2}>
                <Typography component="h2" sx={{ px: 1.5, py: 1, fontSize: 10, letterSpacing: '.12em', textTransform: 'uppercase', color: 'text.secondary', fontWeight: 700 }}>{group.label}</Typography>
                <List disablePadding>{group.items.map(item => <ListItemButton key={item.path} component={NavLink} to={item.path} end={item.path === '/'}
                    onClick={() => setMobileOpen(false)} sx={{ borderRadius: 2, py: 0.8, mb: 0.4,
                        '&.active': { bgcolor: 'rgba(124,77,255,.13)', color: 'primary.light', '& .MuiListItemIcon-root': { color: 'primary.light' } } }}>
                    <ListItemIcon sx={{ minWidth: 36, color: 'text.secondary', '& svg': { fontSize: 20 } }}>{item.icon}</ListItemIcon>
                    <ListItemText primary={item.label} slotProps={{ primary: { fontSize: 13, fontWeight: 500 } }} />
                </ListItemButton>)}</List>
            </Box>)}
        </Box>
        <Divider /><Stack direction="row" alignItems="center" spacing={1.25} p={2}>
            <Avatar sx={{ width: 34, height: 34, bgcolor: 'primary.dark', fontSize: 14 }}>{user?.displayName?.[0] ?? 'U'}</Avatar>
            <Box flex={1} minWidth={0}><Typography variant="body2" noWrap>{user?.displayName || user?.userName}</Typography><Typography variant="caption" color="text.secondary">{isAdmin ? 'Administrator' : 'Listener'}</Typography></Box>
            <IconButton aria-label="Log out" onClick={logout}><Logout fontSize="small" /></IconButton>
        </Stack>
    </Box>;
    return <Box sx={{ display: 'flex', height: '100dvh', overflow: 'hidden' }}>
        <Box component="a" href="#main-content" sx={{ position: 'fixed', left: 8, top: -100, zIndex: 2000, p: 2, bgcolor: 'background.paper', '&:focus': { top: 8 } }}>Skip to content</Box>
        <Box component="nav" aria-label="Main navigation" sx={{ width: { md: drawerWidth }, flexShrink: 0 }}>
            <Drawer variant="temporary" open={mobileOpen} onClose={() => setMobileOpen(false)} sx={{ display: { md: 'none' }, '& .MuiDrawer-paper': { width: drawerWidth } }}>{drawer}</Drawer>
            <Drawer variant="permanent" open sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: drawerWidth } }}>{drawer}</Drawer>
        </Box>
        <Box sx={{ minWidth: 0, flex: 1, display: 'flex', flexDirection: 'column' }}>
            <Stack component="header" direction="row" alignItems="center" spacing={2} sx={{ minHeight: 64, px: { xs: 2, md: 4 }, borderBottom: '1px solid', borderColor: 'divider' }}>
                <IconButton aria-label="Open navigation" onClick={() => setMobileOpen(true)} sx={{ display: { md: 'none' } }}><Menu /></IconButton>
                <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>{section}</Typography>
                <Button color="inherit" startIcon={<Search />} onClick={() => navigate('/search')} sx={{ bgcolor: 'background.paper', color: 'text.secondary', minWidth: { sm: 220 }, justifyContent: 'flex-start' }}>Search your music</Button>
            </Stack>
            <Box component="main" id="main-content" tabIndex={-1} sx={{ flex: 1, minHeight: 0, overflow: 'auto', p: { xs: 2, md: 4 } }}>
                <Box sx={{ maxWidth: 1440, mx: 'auto' }}><Outlet /></Box>
            </Box>
            <NowPlayingBar />
        </Box>
    </Box>;
}
