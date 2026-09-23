import { Autocomplete, CircularProgress, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api } from '../../lib/api';

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
    <Autocomplete
      disabled={disabled}
      options={options.data ?? []}
      value={selected}
      isOptionEqualToValue={(option, candidate) => option.id === candidate.id}
      getOptionLabel={(option) => option.label}
      filterOptions={(items) => items}
      onInputChange={(_, next, reason) => reason === 'input' && setInput(next)}
      onChange={(_, next) => onChange(next?.id ?? null)}
      loading={options.isFetching}
      renderInput={(params) => (
        <TextField
          {...params}
          label={label}
          required={required}
          error={!!error}
          helperText={error ?? helperText}
          slotProps={{
            input: {
              ...params.InputProps,
              endAdornment: (
                <>
                  {options.isFetching && <CircularProgress size={16} />}
                  {params.InputProps.endAdornment}
                </>
              ),
            },
          }}
        />
      )}
    />
  );
}
