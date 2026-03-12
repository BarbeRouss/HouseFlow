"use client";

import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { setAccessToken, getAccessToken } from '../api/client';
import apiClient from '../api/client';

interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
}

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (token: string, user: User) => void;
  logout: () => Promise<void>;
  getToken: () => string | null;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const AUTH_USER_KEY = 'houseflow_auth_user';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    // Load auth state from sessionStorage on mount
    try {
      const userJson = sessionStorage.getItem(AUTH_USER_KEY);
      const hasToken = !!getAccessToken();

      if (userJson && hasToken) {
        // We have both user data and a token - restore the session
        const savedUser = JSON.parse(userJson);
        setUser(savedUser);
      } else if (userJson && !hasToken) {
        // We have user data but no token - try to refresh
        apiClient.post('/api/v1/auth/refresh')
          .then((response) => {
            if (response.data.accessToken) {
              setAccessToken(response.data.accessToken);
              const savedUser = JSON.parse(userJson);
              setUser(savedUser);
            }
          })
          .catch(() => {
            // Refresh failed - clear user
            sessionStorage.removeItem(AUTH_USER_KEY);
            setAccessToken(null);
            setUser(null);
          });
      } else {
        // No stored session
        setUser(null);
      }
    } catch (error) {
      console.error('Failed to load auth state:', error);
      // Clear invalid data
      sessionStorage.removeItem(AUTH_USER_KEY);
      setAccessToken(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const login = (token: string, userData: User) => {
    // Store access token in memory
    setAccessToken(token);

    // Store user data in sessionStorage (refresh tokens are in HttpOnly cookies)
    sessionStorage.setItem(AUTH_USER_KEY, JSON.stringify(userData));
    setUser(userData);
  };

  const logout = async () => {
    try {
      // Call logout endpoint to revoke refresh token
      await apiClient.post('/api/v1/auth/logout');
    } catch (error) {
      console.error('Logout API call failed:', error);
      // Continue with client-side cleanup even if API call fails
    } finally {
      // Clear access token from memory and localStorage
      setAccessToken(null);

      // Clear user data from sessionStorage
      sessionStorage.removeItem(AUTH_USER_KEY);
      setUser(null);
    }
  };

  const getToken = (): string | null => {
    if (typeof window === 'undefined') return null;
    return getAccessToken();
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isLoading,
        login,
        logout,
        getToken,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
