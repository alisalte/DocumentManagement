import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router';
import type { DocumentTypeSchema, FieldSchema, Metadata } from '../../lib/api';
import { formatDate, formatDateTime } from '../../lib/dates';
import { formatNumber } from '../../lib/format';
import { orderedFields } from '../../lib/metadata';
import { Chip } from '../ui';
import { useEntityLabel } from './EntityPicker';

/**
 * Read-only metadata, rendered with the schema version the row was written against. Fields the
 * row has no value for are skipped; values of fields that no longer exist in the schema are shown
 * raw at the end rather than silently dropped.
 */
export function MetadataView({ schema, metadata }: { schema: DocumentTypeSchema | undefined; metadata: Metadata | null }) {
  if (!metadata || Object.keys(metadata).length === 0) {
    return null;
  }

  const known = schema ? orderedFields(schema).filter((field) => metadata[field.code] !== undefined) : [];
  const unknown = Object.keys(metadata).filter((code) => !known.some((field) => field.code === code));

  return (
    <dl className="grid grid-cols-1 gap-x-4 gap-y-1 sm:grid-cols-[minmax(7.5rem,max-content)_minmax(0,1fr)] sm:gap-y-2.5">
      {known.map((field) => (
        <Row key={field.code} label={field.label.fa}>
          <Value field={field} value={metadata[field.code]} />
        </Row>
      ))}
      {unknown.map((code) => (
        <Row key={code} label={code}>
          {String(metadata[code])}
        </Row>
      ))}
    </dl>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="pt-1.5 text-sm text-slate-500 sm:pt-0">{label}</dt>
      <dd className="m-0 min-w-0 text-sm text-slate-800 [overflow-wrap:anywhere]">{children}</dd>
    </>
  );
}

function Value({ field, value }: { field: FieldSchema; value: unknown }) {
  const optionLabel = (option: unknown) =>
    field.options.find((candidate) => candidate.value === option)?.label.fa ?? String(option);

  switch (field.type) {
    case 'Boolean':
      return <>{value ? 'بله' : 'خیر'}</>;
    case 'Date':
      return <>{formatDate(String(value))}</>;
    case 'DateTime':
      return <>{formatDateTime(String(value))}</>;
    case 'Integer':
    case 'Decimal': {
      // Decimals arrive as strings; formatting only groups digits, it never rounds.
      const text = String(value);
      return <>{/^-?\d+(\.\d+)?$/.test(text) ? groupDigits(text) : text}</>;
    }
    case 'Select':
      return <>{optionLabel(value)}</>;
    case 'MultiSelect':
      return (
        <span className="flex flex-wrap gap-1.5">
          {(Array.isArray(value) ? value : []).map((item) => (
            <Chip key={String(item)} size="small" label={optionLabel(item)} />
          ))}
        </span>
      );
    case 'User':
    case 'Group':
      return <EntityName kind={field.type} id={String(value)} />;
    case 'DocumentReference':
      return (
        <RouterLink to={`/documents/${String(value)}`} className="text-brand-700 hover:underline">
          <EntityName kind="DocumentReference" id={String(value)} />
        </RouterLink>
      );
    case 'Url':
      return (
        <a href={String(value)} target="_blank" rel="noopener noreferrer" dir="ltr" className="text-brand-700 hover:underline">
          {String(value)}
        </a>
      );
    default:
      return <span className="whitespace-pre-wrap">{String(value)}</span>;
  }
}

function EntityName({ kind, id }: { kind: 'User' | 'Group' | 'DocumentReference'; id: string }) {
  const label = useEntityLabel(kind, id);
  return <span>{label.data ?? '…'}</span>;
}

/** "12500.75" to "۱۲٬۵۰۰٫۷۵" without ever converting to a float. */
function groupDigits(text: string): string {
  const [whole, fraction] = text.split('.');
  const negative = whole.startsWith('-');
  const digits = negative ? whole.slice(1) : whole;
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, '٬');
  const persian = (value: string) => value.replace(/\d/g, (digit) => formatNumber(Number(digit)));
  return `${negative ? '−' : ''}${persian(grouped)}${fraction ? `٫${persian(fraction)}` : ''}`;
}
