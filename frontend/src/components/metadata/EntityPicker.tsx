import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Combobox } from '../ui';

export type EntityKind = 'User' | 'Group' | 'DocumentReference';

interface Option {
  id: string;
  label: string;
}

/** Id to display name, for read-only views and for the picker's current value. */
export function useEntityLabel(kind: EntityKind, id: string | null | undefined) {
  return useQuery({
    queryKey: ['entity', kind, id],
    enabled: !!id,
    staleTime: 5 * 60_000,
    queryFn: async (): Promise<string> => {
      if (kind === 'DocumentReference') {
        // A reference the viewer may not open resolves to nothing, like any other hidden document.
        return api.document(id!).then((document) => document.title).catch(() => '—');
      }
      const found = await (kind === 'User' ? api.users({ ids: [id!] }) : api.groups({ ids: [id!] }));
      return found[0]?.displayName ?? found[0]?.name ?? '—';
    },
  });
}

async function search(kind: EntityKind, text: string): Promise<Option[]> {
  if (kind === 'DocumentReference') {
    const page = await api.documents({ search: text, pageSize: 20 });
    return page.items.map((document) => ({ id: document.id, label: document.title }));
  }

  const found = kind === 'User' ? await api.users({ search: text }) : await api.groups({ search: text });
  return found.map((entry) => ({
    id: entry.id,
    label: kind === 'User' ? `${entry.displayName} (${entry.username})` : `${entry.name} (${entry.code})`,
  }));
}

interface Props {
  kind: EntityKind;
  label: string;
  value: string | null;
  onChange: (id: string | null) => void;
  required?: boolean;
  error?: string;
  helperText?: string;
  disabled?: boolean;
}

/** Search-as-you-type for people, groups and documents. Only ids are stored. */
export function EntityPicker({ kind, label, value, onChange, required, error, helperText, disabled }: Props) {
  const [input, setInput] = useState('');
  const [debounced, setDebounced] = useState('');
  const current = useEntityLabel(kind, value);

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(input.trim()), 300);
    return () => clearTimeout(handle);
  }, [input]);

  const options = useQuery({
    queryKey: ['entity-search', kind, debounced],
    queryFn: () => search(kind, debounced),
  });

  const selected: Option | null = value ? { id: value, label: current.data ?? '…' } : null;

  return (
    <Combobox
      label={label}
      value={selected}
      onChange={(option) => onChange(option?.id ?? null)}
      options={options.data ?? []}
      onInputChange={setInput}
      loading={options.isFetching}
      required={required}
      error={!!error}
      helperText={error ?? helperText}
      disabled={disabled}
    />
  );
}
