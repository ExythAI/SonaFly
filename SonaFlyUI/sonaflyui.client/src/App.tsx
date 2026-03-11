import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ThemeProvider, CssBaseline } from '@mui/material';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import theme from './theme/theme';
import { AuthProvider, useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import { PlayerProvider } from './components/PlayerContext';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import LibraryRootsPage from './pages/LibraryRootsPage';
import UsersPage from './pages/UsersPage';
import { ArtistsPage, AlbumsPage, TracksPage } from './pages/BrowsePages';
import AlbumDetailPage from './pages/AlbumDetailPage';
import SearchPage from './pages/SearchPage';
import PlaylistsPage from './pages/PlaylistsPage';
import MixedTapePage from './pages/MixedTapePage';
import SystemPage from './pages/SystemPage';
import RestrictionsPage from './pages/RestrictionsPage';
import AuditoriumsPage from './pages/AuditoriumsPage';

const queryClient = new QueryClient({
    defaultOptions: {
        queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30000 },
    },
});

const PrivateRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { isAuthenticated, loading } = useAuth();
    if (loading) return null;
    return isAuthenticated ? <>{children}</> : <Navigate to="/login" />;
};

const AdminRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { isAdmin, loading } = useAuth();
    if (loading) return null;
    return isAdmin ? <>{children}</> : <Navigate to="/" />;
};

const App: React.FC = () => (
    <QueryClientProvider client={queryClient}>
        <ThemeProvider theme={theme}>
            <CssBaseline />
            <AuthProvider>
                <PlayerProvider>
                <BrowserRouter>
                    <Routes>
                        <Route path="/login" element={<LoginPage />} />
                        <Route path="/" element={<PrivateRoute><Layout /></PrivateRoute>}>
                            <Route index element={<DashboardPage />} />
                            <Route path="library-roots" element={<AdminRoute><LibraryRootsPage /></AdminRoute>} />
                            <Route path="users" element={<AdminRoute><UsersPage /></AdminRoute>} />
                            <Route path="artists" element={<ArtistsPage />} />
                            <Route path="albums" element={<AlbumsPage />} />
                            <Route path="albums/:id" element={<AlbumDetailPage />} />
                            <Route path="tracks" element={<TracksPage />} />
                            <Route path="search" element={<SearchPage />} />
                            <Route path="playlists" element={<PlaylistsPage />} />
                            <Route path="mixed-tapes" element={<MixedTapePage />} />
                            <Route path="system" element={<SystemPage />} />
                            <Route path="restrictions" element={<AdminRoute><RestrictionsPage /></AdminRoute>} />
                            <Route path="auditoriums" element={<AdminRoute><AuditoriumsPage /></AdminRoute>} />
                        </Route>
                    </Routes>
                </BrowserRouter>
                </PlayerProvider>
            </AuthProvider>
        </ThemeProvider>
    </QueryClientProvider>
);

export default App;
