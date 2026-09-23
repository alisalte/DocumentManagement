import {
  Autocomplete,
  Chip,
  FormControlLabel,
  FormHelperText,
  MenuItem,
  Stack,
  Switch,
  TextField,
} from '@mui/material';
import { useEffect, useMemo, useState } from 'react';
import type { DocumentTypeSchema, FieldSchema, Metadata } from '../../lib/api';
import { formatDate, parseJalaliDate, toJalaliInput } from '../../lib/dates';
import { evaluateForm, orderedFields } from '../../lib/metadata';
import { EntityPicker } from './EntityPicker';

interface Props {
  schema: DocumentTypeSchema;
  value: Metadata;
  onChange: (value: Metadata) => void;
  /** Per-field messages, from the server or from clientErrors. */
  errors?: Record<string, string[]>;
  disabled?: boolean;
}

/**
 * Renders a document type's schema as a form. Visibility and "required" follow the schema's rules
 * live (lib/metadata.ts); hidden fields are not rendered and never submitted.
 */
export function DynamicForm({ schema, value, onChange, errors = {}, disabled }: Props) {
  const state = useMemo(() => evaluateForm(schema, value), [schema, value]);
  const fields = orderedFields(schema).filter((field) => state.visible.has(field.code));

  const set = (code: string, next: unknown) => onChange({ ...value, [code]: next });

  return (
    <Stack spacing={2}>
      {fields.map((field) => (
        <FieldInput
          key={field.code}
          field={field}
          value={value[field.code]}
          onChange={(next) => set(field.code, next)}
          required={state.required.has(field.code)}
          error={errors[field.code]?.join(' ')}
          disabled={disabled}
        />
      ))}
    </Stack>
  );
}

interface FieldProps {
  field: FieldSchema;
  value: unknown;
  onChange: (value: unknown) => void;
  required: boolean;
  error?: string;
  disabled?: boolean;
}

function FieldInput({ field, value, onChange, required, error, disabled }: FieldProps) {
  const label = field.label.fa;
  const helperText = error ?? field.helpText?.fa ?? undefined;
  const common = { label, required, error: !!error, helperText, disabled, fullWidth: true };
  const text = typeof value === 'string' || typeof value === 'number' ? String(value) : '';
  const activeOptions = field.options.filter((option) => option.isActive);

  switch (field.type) {
    case 'LongText':
      return <TextField {...common} multiline minRows={3} value={text} onChange={(event) => onChange(event.target.value)} />;

    case 'Integer':
    case 'Decimal':
      // Kept as text: the server accepts Persian digits and separators, and a decimal must never
      // pass through a JavaScript number on its way there.
      return (
        <TextField
          {...common}
          value={text}
          onChange={(event) => onChange(event.target.value)}
          slotProps={{ htmlInput: { inputMode: field.type === 'Integer' ? 'numeric' : 'decimal', dir: 'ltr' } }}
        />
      );

    case 'Boolean':
      return (
        <Stack>
          <FormControlLabel
            disabled={disabled}
            control={<Switch checked={value === true} onChange={(event) => onChange(event.target.checked)} />}
            label={label}
          />
          {helperText && <FormHelperText error={!!error}>{helperText}</FormHelperText>}
        </Stack>
      );

    case 'Date':
      return <JalaliDateField {...common} value={typeof value === 'string' ? value : ''} onChange={onChange} />;

    case 'DateTime':
      return (
        <TextField
          {...common}
          type="datetime-local"
          value={toLocalInput(typeof value === 'string' ? value : '')}
          onChange={(event) => onChange(event.target.value ? new Date(event.target.value).toISOString() : '')}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      );

    case 'Select':
      return (
        <TextField {...common} select value={text} onChange={(event) => onChange(event.target.value)}>
          {!required && <MenuItem value="">—</MenuItem>}
          {activeOptions.map((option) => (
            <MenuItem key={option.value} value={option.value}>
              {option.label.fa}
            </MenuItem>
          ))}
        </TextField>
      );

    case 'MultiSelect': {
      const selected = Array.isArray(value) ? (value as string[]) : [];
      return (
        <Autocomplete
          multiple
          disabled={disabled}
          options={activeOptions.map((option) => option.value)}
          getOptionLabel={(option) => field.options.find((candidate) => candidate.value === option)?.label.fa ?? option}
          value={selected}
          onChange={(_, next) => onChange(next)}
          renderValue={(items, getItemProps) =>
            items.map((item, index) => {
              const { key, ...itemProps } = getItemProps({ index });
              const option = field.options.find((candidate) => candidate.value === item);
              return <Chip key={key} size="small" label={option?.label.fa ?? item} {...itemProps} />;
            })
          }
          renderInput={(params) => (
            <TextField {...params} label={label} required={required} error={!!error} helperText={helperText} />
          )}
        />
      );
    }

    case 'User':
    case 'Group':
    case 'DocumentReference':
      return (
        <EntityPicker
          kind={field.type}
          label={label}
          value={typeof value === 'string' ? value : null}
          onChange={onChange}
          required={required}
          error={error}
          helperText={field.helpText?.fa ?? undefined}
          disabled={disabled}
        />
      );

    case 'Url':
    case 'Email':
    case 'Phone':
      return (
        <TextField
          {...common}
          type={field.type === 'Email' ? 'email' : field.type === 'Url' ? 'url' : 'tel'}
          value={text}
          onChange={(event) => onChange(event.target.value)}
          slotProps={{ htmlInput: { dir: 'ltr' } }}
        />
      );

    default:
      return (
        <TextField
          {...common}
          value={text}
          onChange={(event) => onChange(event.target.value)}
          slotProps={{ htmlInput: { maxLength: field.validation.maxLength ?? 1000 } }}
        />
      );
  }
}

/**
 * Typed in Jalali ("1403/01/31", Persian digits welcome), stored as a Gregorian ISO date. While the
 * text is not a valid date the raw text is passed up, so the form's own check can flag it.
 */
function JalaliDateField({
  value,
  onChange,
  helperText,
  ...rest
}: {
  value: string;
  onChange: (value: unknown) => void;
  label: string;
  required: boolean;
  error: boolean;
  helperText?: string;
  disabled?: boolean;
  fullWidth: boolean;
}) {
  const [text, setText] = useState(() => toJalaliInput(value) || value);

  useEffect(() => {
    // Follow outside changes (a reset, a loaded document) without fighting the user's typing.
    if (/^\d{4}-\d{2}-\d{2}$/.test(value) && parseJalaliDate(text) !== value) setText(toJalaliInput(value));
    if (!value && text && parseJalaliDate(text)) setText('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  const iso = parseJalaliDate(text);
  return (
    <TextField
      {...rest}
      value={text}
      placeholder="۱۴۰۳/۰۱/۳۱"
      onChange={(event) => {
        setText(event.target.value);
        onChange(parseJalaliDate(event.target.value) ?? event.target.value);
      }}
      helperText={helperText ?? (iso ? formatDate(iso) : undefined)}
      slotProps={{ htmlInput: { dir: 'ltr', inputMode: 'numeric' } }}
    />
  );
}

function toLocalInput(iso: string): string {
  const date = new Date(iso);
  if (!iso || Number.isNaN(date.getTime())) return '';
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}
