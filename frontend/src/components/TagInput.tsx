import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api } from '../lib/api';
import { t } from '../strings';
import { ChipsInput } from './ui';

interface Props {
  value: string[];
  onChange: (tags: string[]) => void;
  disabled?: boolean;
}

/** Free text with suggestions from existing tags; the server folds Arabic/Persian variants. */
export function TagInput({ value, onChange, disabled }: Props) {
  const [input, setInput] = useState('');
  const [debounced, setDebounced] = useState('');

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(input.trim()), 250);
    return () => clearTimeout(handle);
  }, [input]);

  const suggestions = useQuery({
    queryKey: ['tags', debounced],
    queryFn: () => api.tags(debounced),
    enabled: debounced.length > 0,
  });

  return (
    <ChipsInput
      label={t.tags}
      helperText={t.tagsHelp}
      value={value}
      onChange={(next) => onChange(next.map((tag) => tag.trim()).filter(Boolean))}
      suggestions={(suggestions.data ?? []).map((tag) => tag.name)}
      onInputChange={setInput}
      loading={suggestions.isFetching}
      disabled={disabled}
    />
  );
}
