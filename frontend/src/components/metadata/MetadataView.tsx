import { Box, Chip, Link, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { DocumentTypeSchema, FieldSchema, Metadata } from '../../lib/api';
import { formatDate, formatDateTime } from '../../lib/dates';
import { formatNumber } from '../../lib/format';
import { orderedFields } from '../../lib/metadata';
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
    <Box
      component="dl"
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: '1fr', sm: 'minmax(120px, max-content) 1fr' },
        columnGap: 2,
        rowGap: { xs: 0.25, sm: 1 },
        m: 0,
      }}
    >
      {known.map((field) => (
        <Row key={field.code} label={field.label.fa}>
          <Value field={field} value={metadata[field.code]} />
        </Row>
      ))}
      {unknown.map((code) => (
        <Row key={code} label={code}>
          <Typography variant="body2">{String(metadata[code])}</Typography>
        </Row>
      ))}
    </Box>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <Typography component="dt" variant="body2" color="text.secondary" sx={{ pt: { xs: 1, sm: 0 } }}>
        {label}
      </Typography>
      <Box component="dd" sx={{ m: 0, overflowWrap: 'anywhere' }}>
        {children}
      </Box>
    </>
  );
}

function Value({ field, value }: { field: FieldSchema; value: unknown }) {
  const optionLabel = (option: unknown) =>
    field.options.find((candidate) => candidate.value === option)?.label.fa ?? String(option);

  switch (field.type) {
    case 'Boolean':
      return <Typography variant="body2">{value ? 'بله' : 'خیر'}</Typography>;
    case 'Date':
      return <Typography variant="body2">{formatDate(String(value))}</Typography>;
    case 'DateTime':
      return <Typography variant="body2">{formatDateTime(String(value))}</Typography>;
    case 'Integer':
    case 'Decimal': {
      // Decimals arrive as strings; formatting only groups digits, it never rounds.
      const text = String(value);
      return <Typography variant="body2">{/^-?\d+(\.\d+)?$/.test(text) ? groupDigits(text) : text}</Typography>;
    }
    case 'Select':
      return <Typography variant="body2">{optionLabel(value)}</Typography>;
    case 'MultiSelect':
      return (
        <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
          {(Array.isArray(value) ? value : []).map((item) => (
            <Chip key={String(item)} size="small" label={optionLabel(item)} />
          ))}
        </Stack>
      );
    case 'User':
    case 'Group':
      return <EntityName kind={field.type} id={String(value)} />;
    case 'DocumentReference':
      return (
        <Link component={RouterLink} to={`/documents/${String(value)}`}>
          <EntityName kind="DocumentReference" id={String(value)} />
        </Link>
      );
    case 'Url':
      return (
        <Link href={String(value)} target="_blank" rel="noopener noreferrer" dir="ltr">
          {String(value)}
        </Link>
      );
    default:
      return <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>{String(value)}</Typography>;
  }
}

function EntityName({ kind, id }: { kind: 'User' | 'Group' | 'DocumentReference'; id: string }) {
  const label = useEntityLabel(kind, id);
  return <Typography variant="body2" component="span">{label.data ?? '…'}</Typography>;
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
