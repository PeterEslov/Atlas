import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { authApi } from "../api/auth";
import { onUnauthorized } from "../api/client";
import type { LoginRequest, RegisterRequest } from "../api/types";
import { clearSession, hasPermission, loadSession, saveSession, type Session } from "./session";

interface AuthContextValue {
  session: Session | null;
  isAuthenticated: boolean;
  login: (request: LoginRequest) => Promise<void>;
  register: (request: RegisterRequest) => Promise<void>;
  logout: () => void;
  can: (permission: string) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => loadSession());

  useEffect(() => {
    // Fires if a request comes back 401 mid-session (expired/invalid token) —
    // see client.ts. Without this, a stale session would sit in localStorage
    // "logged in" while every API call silently fails until the user
    // refreshes the page by hand.
    return onUnauthorized(() => setSession(null));
  }, []);

  const login = useCallback(async (request: LoginRequest) => {
    const auth = await authApi.login(request);
    setSession(saveSession(auth));
  }, []);

  const register = useCallback(async (request: RegisterRequest) => {
    const auth = await authApi.register(request);
    setSession(saveSession(auth));
  }, []);

  const logout = useCallback(() => {
    clearSession();
    setSession(null);
  }, []);

  const can = useCallback((permission: string) => hasPermission(session, permission), [session]);

  const value = useMemo<AuthContextValue>(
    () => ({ session, isAuthenticated: session !== null, login, register, logout, can }),
    [session, login, register, logout, can],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}
