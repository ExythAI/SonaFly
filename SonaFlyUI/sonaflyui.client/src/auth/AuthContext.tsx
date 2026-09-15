import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import axios from 'axios';
import { useQueryClient } from '@tanstack/react-query';
import { errorMessage } from '../components/Feedback';
import { authApi, restoreSession } from '../api/client';
import { resetSession, setAccessToken } from '../api/session';

interface User {
    id: string;
    userName: string;
    email: string;
    displayName: string;
    roles: string[];
    /** Set while the account still holds a bootstrap or admin-assigned password. */
    mustChangePassword?: boolean;
}

interface AuthContextType {
    user: User | null;
    isAuthenticated: boolean;
    isAdmin: boolean;
    login: (username: string, password: string) => Promise<void>;
    logout: () => Promise<void>;
    /** True while the server will reject every request except the password change. */
    mustChangePassword: boolean;
    changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
    loading: boolean;
    sessionError: string | null;
    retrySession: () => void;
}

const AuthContext = createContext<AuthContextType>(null!);

export const useAuth = () => useContext(AuthContext);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [user, setUser] = useState<User | null>(null);
    const [loading, setLoading] = useState(true);
    const [sessionError, setSessionError] = useState<string | null>(null);
    const queryClient = useQueryClient();

    const loadUser = useCallback(async () => {
        setLoading(true);
        setSessionError(null);
        try {
            // The access token only ever lives in memory, so on a cold load it has to be
            // re-obtained from the refresh cookie before anything else can be asked.
            const token = await restoreSession();
            if (!token) { setUser(null); return; }
            const res = await authApi.me();
            setUser(res.data);
        } catch (error) {
            if (axios.isAxiosError(error) && error.response?.status === 401) {
                resetSession();
                setUser(null);
            } else setSessionError(errorMessage(error));
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { loadUser(); }, [loadUser]);

    const login = async (username: string, password: string) => {
        // Start a new session generation first, so a refresh still in flight from the
        // previous one cannot overwrite these credentials.
        resetSession();
        const res = await authApi.login(username, password);
        setAccessToken(res.data.accessToken);
        queryClient.clear();
        setSessionError(null);
        setUser(res.data.user);
    };

    const changePassword = async (currentPassword: string, newPassword: string) => {
        // Changing the password revokes the current tokens, so the server hands back a
        // replacement pair. Store it before any further request goes out.
        const res = await authApi.changePassword(currentPassword, newPassword);
        setAccessToken(res.data.accessToken);
        const me = await authApi.me();
        setUser(me.data);
    };

    const logout = async () => {
        // The server revokes the token family and clears the cookie; this clears the
        // in-memory token even if that call fails.
        try { await authApi.logout(); } catch { /* ignore */ }
        resetSession();
        queryClient.clear();
        setSessionError(null);
        setUser(null);
    };

    return (
        <AuthContext.Provider value={{
            user,
            isAuthenticated: !!user,
            isAdmin: user?.roles?.includes('Admin') ?? false,
            mustChangePassword: user?.mustChangePassword ?? false,
            login, logout, changePassword, loading, sessionError,
            retrySession: () => { void loadUser(); }
        }}>
            {children}
        </AuthContext.Provider>
    );
};
