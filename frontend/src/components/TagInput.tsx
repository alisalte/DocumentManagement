import { Autocomplete, Chip, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api } from '../lib/api';
import { t } from '../strings';

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
    <Autocomplete
      multiple
      freeSolo
      disabled={disabled}
      options={(suggestions.data ?? []).map((tag) => tag.name)}
      value={value}
      inputValue={input}
      onInputChange={(_, next) => setInput(next)}
      onChange={(_, next) => onChange(next.map((tag) => tag.trim()).filter(Boolean))}
      renderValue={(selected, getItemProps) =>
        selected.map((tag, index) => {
          const { key, ...itemProps } = getItemProps({ index });
          return <Chip key={key} label={tag} size="small" {...itemProps} />;
        })
      }
      renderInput={(params) => <TextField {...params} label={t.tags} helperText={t.tagsHelp} />}
    />
  );
}
