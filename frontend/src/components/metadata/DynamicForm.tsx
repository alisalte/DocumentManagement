import { useEffect, useMemo, useState } from 'react';
import type { DocumentTypeSchema, FieldSchema, Metadata } from '../../lib/api';
import { formatDate, parseJalaliDate, toJalaliInput } from '../../lib/dates';
import { evaluateForm, orderedFields } from '../../lib/metadata';
import { Checkbox, Select, Switch, TextArea, TextField, cx } from '../ui';
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
    <div className="space-y-4">
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
    </div>
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
  const common = { label, required, error: !!error, helperText, disabled };
  const text = typeof value === 'string' || typeof value === 'number' ? String(value) : '';
  const activeOptions = field.options.filter((option) => option.isActive);

  switch (field.type) {
    case 'LongText':
      return <TextArea {...common} rows={3} value={text} onChange={(event) => onChange(event.target.value)} />;

    case 'Integer':
    case 'Decimal':
      // Kept as text: the server accepts Persian digits and separators, and a decimal must never
      // pass through a JavaScript number on its way there.
      return (
        <TextField
          {...common}
          value={text}
          inputMode={field.type === 'Integer' ? 'numeric' : 'decimal'}
          dir="ltr"
          onChange={(event) => onChange(event.target.value)}
        />
      );

    case 'Boolean':
      return (
        <div className="w-full">
          <Switch
            label={label}
            checked={value === true}
            onChange={(event) => onChange(event.target.checked)}
            disabled={disabled}
          />
          {helperText && (
            <p className={cx('mt-1.5 text-xs', error ? 'text-rose-600' : 'text-paper-500')}>{helperText}</p>
          )}
        </div>
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
        />
      );

    case 'Select':
      return (
        <Select {...common} value={text} onChange={(event) => onChange(event.target.value)}>
          {!required && <option value="">—</option>}
          {activeOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label.fa}
            </option>
          ))}
        </Select>
      );

    case 'MultiSelect':
      return (
        <MultiSelectField
          label={label}
          required={required}
          options={activeOptions}
          selected={Array.isArray(value) ? (value as string[]) : []}
          onChange={onChange}
          helperText={helperText}
          error={!!error}
          disabled={disabled}
        />
      );

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
          dir="ltr"
          onChange={(event) => onChange(event.target.value)}
        />
      );

    default:
      return (
        <TextField
          {...common}
          value={text}
          maxLength={field.validation.maxLength ?? 1000}
          onChange={(event) => onChange(event.target.value)}
        />
      );
  }
}

/** The option list is fixed by the schema, so every choice is visible as a checkbox. */
function MultiSelectField({
  label,
  required,
  options,
  selected,
  onChange,
  helperText,
  error,
  disabled,
}: {
  label: string;
  required: boolean;
  options: FieldSchema['options'];
  selected: string[];
  onChange: (value: unknown) => void;
  helperText?: string;
  error: boolean;
  disabled?: boolean;
}) {
  return (
    <div className="w-full">
      <label className="mb-1.5 block text-sm font-medium text-ink-800">
        {label}
        {required && <span className="text-rose-500"> *</span>}
      </label>
      <div className="flex flex-wrap gap-x-4 gap-y-2 rounded-lg border border-paper-300 bg-white px-3.5 py-2.5 shadow-sm">
        {options.map((option) => (
          <Checkbox
            key={option.value}
            label={option.label.fa ?? option.value}
            checked={selected.includes(option.value)}
            disabled={disabled}
            onChange={(event) =>
              onChange(
                event.target.checked
                  ? [...selected, option.value]
                  : selected.filter((item) => item !== option.value),
              )
            }
          />
        ))}
      </div>
      {helperText && (
        <p className={cx('mt-1.5 text-xs', error ? 'text-rose-600' : 'text-paper-500')}>{helperText}</p>
      )}
    </div>
  );
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
      inputMode="numeric"
      dir="ltr"
    />
  );
}

function toLocalInput(iso: string): string {
  const date = new Date(iso);
  if (!iso || Number.isNaN(date.getTime())) return '';
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}
