import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { addPage, deletePage, movePage } from './scanner.service';
import type { PreparedScanFile, ScanPage, ScanSession } from './scanner.types';

interface ScanContextValue extends ScanSession {
  add(page: ScanPage): void;
  remove(id: string): void;
  move(id: string, direction: -1 | 1): void;
  setPrepared(file: PreparedScanFile | undefined): void;
  reset(): void;
}

const ScanContext = createContext<ScanContextValue | null>(null);

export function ScanSessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<ScanSession>({ pages: [] });

  const value = useMemo<ScanContextValue>(
    () => ({
      ...session,
      add: (page) => setSession((current) => ({ pages: addPage(current.pages, page) })),
      remove: (id) => setSession((current) => ({ pages: deletePage(current.pages, id) })),
      move: (id, direction) =>
        setSession((current) => ({ pages: movePage(current.pages, id, direction) })),
      setPrepared: (file) => setSession((current) => ({ ...current, prepared: file })),
      reset: () => setSession({ pages: [] }),
    }),
    [session],
  );

  return <ScanContext.Provider value={value}>{children}</ScanContext.Provider>;
}

export function useScanSession(): ScanContextValue {
  const value = useContext(ScanContext);
  if (!value) throw new Error('useScanSession must be used inside ScanSessionProvider');
  return value;
}
