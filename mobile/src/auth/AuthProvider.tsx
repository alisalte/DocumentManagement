import { createContext, useContext, useEffect, useRef, useSyncExternalStore, type ReactNode } from 'react';
import * as authApi from '../api/auth.api';
import { bindSession } from '../api/client';
import { isDemoPreview } from '../config/demo';
import { secureTokenStorage } from '../storage/secure-storage';
import { createAuthController, type AuthController, type AuthState } from './auth.store';

interface AuthContextValue extends AuthState {
  login: AuthController['login'];
  logout: AuthController['logout'];
  refreshSession: AuthController['refreshSession'];
  clearLocal: AuthController['clearLocal'];
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const controllerRef = useRef<AuthController | null>(null);
  if (!controllerRef.current) {
    const controller = createAuthController({ storage: secureTokenStorage, api: authApi });
    controllerRef.current = controller;
    bindSession({
      getAccessToken: () => controller.getAccessToken(),
      refresh: () => controller.refreshSession(),
      onSessionExpired: () => {
        void controller.clearLocal();
      },
    });
  }

  const controller = controllerRef.current;
  const state = useSyncExternalStore(controller.subscribe, controller.getState, controller.getState);

  useEffect(() => {
    if (isDemoPreview()) {
      controller.enterDemo();
      return;
    }
    void controller.restoreSession().catch(() => controller.clearLocal());
  }, [controller]);

  return (
    <AuthContext.Provider
      value={{
        ...state,
        login: controller.login,
        logout: controller.logout,
        refreshSession: controller.refreshSession,
        clearLocal: controller.clearLocal,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside AuthProvider');
  return value;
}
