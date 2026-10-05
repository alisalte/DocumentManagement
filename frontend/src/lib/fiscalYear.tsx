import { useQuery } from '@tanstack/react-query';
import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { api, type FiscalYearOption, type FiscalYearOverview } from './api';
import { useSession } from '../session';

const storageKey = 'dms.fiscalYear';

const yearDigits = new Intl.NumberFormat('fa-IR', { useGrouping: false });

export function formatFiscalYear(year: number): string {
  return yearDigits.format(year);
}

interface FiscalYearState {
  currentYear: number;
  selectedYear: number;
  years: FiscalYearOption[];
  /** The selected year is before the current Jalali year: browse and report only. */
  closed: boolean;
  ready: boolean;
  failed: boolean;
  selectYear: (year: number) => void;
}

const FiscalYearContext = createContext<FiscalYearState | null>(null);

function readStoredYear(): number | null {
  const raw = localStorage.getItem(storageKey);
  const parsed = raw ? Number(raw) : NaN;
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}

function isDemoPreview(): boolean {
  return import.meta.env.DEV && new URLSearchParams(window.location.search).get('demo') === '1';
}

const demoOverview: FiscalYearOverview = {
  currentYear: 1405,
  years: [
    { year: 1405, status: 'Open', documentCount: 2 },
    { year: 1404, status: 'Closed', documentCount: 1 },
  ],
};

export function FiscalYearProvider({ children }: { children: ReactNode }) {
  const { user } = useSession();
  const demo = isDemoPreview();
  const overview = useQuery({
    queryKey: ['fiscal-years'],
    queryFn: api.fiscalYears,
    enabled: !!user && !demo,
    staleTime: 60_000,
  });
  const [stored, setStored] = useState<number | null>(readStoredYear);

  const value = useMemo<FiscalYearState>(() => {
    const data = demo ? demoOverview : overview.data;
    const years = data?.years ?? [];
    const currentYear = data?.currentYear ?? 0;
    const storedIsListed = stored != null && years.some((year) => year.year === stored);
    const selectedYear = storedIsListed ? stored : currentYear;
    const selected = years.find((year) => year.year === selectedYear);
    return {
      currentYear,
      selectedYear,
      years,
      closed: selected?.status === 'Closed',
      ready: overview.isSuccess || demo,
      failed: overview.isError,
      selectYear: (year: number) => {
        setStored(year);
        localStorage.setItem(storageKey, String(year));
      },
    };
  }, [demo, overview.data, overview.isError, overview.isSuccess, stored]);

  return <FiscalYearContext.Provider value={value}>{children}</FiscalYearContext.Provider>;
}

export function useFiscalYear(): FiscalYearState {
  const value = useContext(FiscalYearContext);
  if (!value) {
    throw new Error('useFiscalYear must be used inside FiscalYearProvider');
  }
  return value;
}
