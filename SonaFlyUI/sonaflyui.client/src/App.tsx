import React, { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ThemeProvider, CssBaseline } from '@mui/material';
import { QueryClient, QueryClientProvider, MutationCache } from '@tanstack/react-query';
import theme from './theme/theme';
import { PageLoading, QueryError } from './components/PageParts';
import { FeedbackProvider, notify, errorMessage } from './components/Feedback';
import { AuthProvider, useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import { AuditoriumProvider } from './components/AuditoriumContext';
import { PlayerProvider } from './components/PlayerContext';
import LoginPage from './pages/LoginPage';
import ChangePasswordPage from './pages/ChangePasswordPage';
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const LibraryRootsPage = lazy(() => import('./pages/LibraryRootsPage'));
const UsersPage = lazy(() => import('./pages/UsersPage'));
const ArtistsPage = lazy(() => import('./pages/BrowsePages').then(m => ({ default: m.ArtistsPage })));
const AlbumsPage = lazy(() => import('./pages/BrowsePages').then(m => ({ default: m.AlbumsPage })));
const TracksPage = lazy(() => import('./pages/BrowsePages').then(m => ({ default: m.TracksPage })));
const AlbumDetailPage = lazy(() => import('./pages/AlbumDetailPage'));
const ArtistDetailPage = lazy(() => import('./pages/ArtistDetailPage'));
const SearchPage = lazy(() => import('./pages/SearchPage'));
const PlaylistsPage = lazy(() => import('./pages/PlaylistsPage'));
const MixedTapePage = lazy(() => import('./pages/MixedTapePage'));
const SystemPage = lazy(() => import('./pages/SystemPage'));
const RestrictionsPage = lazy(() => import('./pages/RestrictionsPage'));
const AuditoriumsPage = lazy(() => import('./pages/AuditoriumsPage'));

const queryClient = new QueryClient({
    mutationCache: new MutationCache({
        onError: error => { notify(errorMessage(error), 'error'); },
        onSuccess: (_data, _variables, _result, mutation) => { notify(String(mutation.meta?.successMessage ?? 'Changes saved.')); },
    }),
    defaultOptions: {
        queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30000 },
    },
});

const PrivateRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { isAuthenticated, mustChangePassword, loading, sessionError, retrySession } = useAuth();
    if (loading) return <PageLoading />;
    if (sessionError) return <QueryError error={new Error(sessionError)} retry={retrySession} />;
    if (!isAuthenticated) return <Navigate to="/login" />;
    // The server refuses every other endpoint until the temporary password is replaced,
    // so there is nothing useful to render behind this.
    if (mustChangePassword) return <ChangePasswordPage />;
    return <>{children}</>;
};

const AdminRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { isAdmin, loading } = useAuth();
    if (loading) return <PageLoading />;
    return isAdmin ? <>{children}</> : <Navigate to="/" />;
};

const App: React.FC = () => (
    <ThemeProvider theme={theme}><FeedbackProvider><QueryClientProvider client={queryClient}>
            <CssBaseline />
            <AuthProvider>
                <PlayerProvider><AuditoriumProvider>
                <BrowserRouter>
                    <Routes>
                        <Route path="/login" element={<LoginPage />} />
                        <Route path="/" element={<PrivateRoute><Suspense fallback={<PageLoading />}><Layout /></Suspense></PrivateRoute>}>
                            <Route index element={<DashboardPage />} />
                            <Route path="library-roots" element={<AdminRoute><LibraryRootsPage /></AdminRoute>} />
                            <Route path="users" element={<AdminRoute><UsersPage /></AdminRoute>} />
                            <Route path="artists" element={<ArtistsPage />} />
                            <Route path="artists/:id" element={<ArtistDetailPage />} />
                            <Route path="albums" element={<AlbumsPage />} />
                            <Route path="albums/:id" element={<AlbumDetailPage />} />
                            <Route path="tracks" element={<TracksPage />} />
                            <Route path="search" element={<SearchPage />} />
                            <Route path="playlists" element={<PlaylistsPage />} />
                            <Route path="mixed-tapes" element={<MixedTapePage />} />
                            <Route path="system" element={<SystemPage />} />
                            <Route path="restrictions" element={<AdminRoute><RestrictionsPage /></AdminRoute>} />
                            <Route path="auditoriums" element={<AuditoriumsPage />} />
                        </Route>
                    </Routes>
                </BrowserRouter>
                </AuditoriumProvider></PlayerProvider>
            </AuthProvider>
    </QueryClientProvider></FeedbackProvider></ThemeProvider>
);

export default App;
