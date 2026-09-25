import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, type CurrentUser } from './lib/api';

interface Session {
  user: CurrentUser | null;
  ready: boolean;
  signIn: (username: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
  /** Changes the password, which ends every session, and signs in again with the new one. */
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
}

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [ready, setReady] = useState(false);

  // A reload drops the in-memory access token; the refresh token in sessionStorage restores it.
  useEffect(() => {
    let cancelled = false;
    api
      .restoreSession()
      .then(async (restored) => (restored ? api.me() : null))
      .then((me) => !cancelled && setUser(me))
      .catch(() => !cancelled && setUser(null))
      .finally(() => !cancelled && setReady(true));

    return () => {
      cancelled = true;
    };
  }, []);

  const signIn = useCallback(async (username: string, password: string) => {
    await api.login(username, password);
    setUser(await api.me());
  }, []);

  const signOut = useCallback(async () => {
    await api.logout();
    setUser(null);
  }, []);

  const changePassword = useCallback(
    async (currentPassword: string, newPassword: string) => {
      if (!user) return;
      await api.changePassword(currentPassword, newPassword);
      await api.login(user.username, newPassword);
      setUser(await api.me());
    },
    [user],
  );

  return <SessionContext.Provider value={{ user, ready, signIn, signOut, changePassword }}>{children}</SessionContext.Provider>;
}

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) {
    throw new Error('useSession must be used inside SessionProvider');
  }

  return session;
}
