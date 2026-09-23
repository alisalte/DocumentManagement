import { describe, expect, it } from 'vitest';
import type { DocumentTypeSchema, FieldSchema } from './api';
import { clientErrors, evaluateForm, toSubmission } from './metadata';

const field = (code: string, type: FieldSchema['type'], extra: Partial<FieldSchema> = {}): FieldSchema => ({
  code,
  label: { fa: code },
  type,
  isRequired: false,
  isSearchable: false,
  isSortable: false,
  showInList: false,
  isApprovalRelevant: false,
  validation: {},
  options: [],
  displayOrder: 0,
  isActive: true,
  ...extra,
});

const schema: DocumentTypeSchema = {
  documentTypeId: 't',
  versionId: 'v',
  versionNumber: 1,
  isPublished: true,
  fields: [
    field('kind', 'Select', { isRequired: true }),
    field('amount', 'Decimal'),
    field('vendor', 'Text'),
    field('vendor_email', 'Email'),
    field('approver', 'User'),
    field('signed_on', 'Date'),
  ],
  rules: [
    { kind: 'Show', targets: ['vendor'], condition: { field: 'kind', op: 'eq', value: 'contract' }, displayOrder: 0 },
    { kind: 'Show', targets: ['vendor_email'], condition: { not: { field: 'vendor', op: 'is_empty' } }, displayOrder: 1 },
    { kind: 'Require', targets: ['approver'], condition: { field: 'amount', op: 'gt', value: 10000000000 }, displayOrder: 2 },
  ],
};

describe('dynamic form state', () => {
  it('shows a field only while its rule holds, and cascades', () => {
    const letter = evaluateForm(schema, { kind: 'letter', vendor: 'ACME' });
    expect(letter.visible.has('vendor')).toBe(false);
    expect(letter.visible.has('vendor_email')).toBe(false);

    const contract = evaluateForm(schema, { kind: 'contract', vendor: 'ACME' });
    expect(contract.visible.has('vendor_email')).toBe(true);
  });

  it('reads amounts typed with Persian digits and separators', () => {
    expect(evaluateForm(schema, { kind: 'contract', amount: '۱۲٬۰۰۰٬۰۰۰٬۰۰۰' }).required.has('approver')).toBe(true);
    expect(evaluateForm(schema, { kind: 'contract', amount: '8000000000' }).required.has('approver')).toBe(false);
  });

  it('never submits hidden or blank values', () => {
    expect(toSubmission(schema, { kind: 'letter', vendor: 'stale', amount: '' })).toEqual({ kind: 'letter' });
  });

  it('flags missing required fields and unparsed dates before asking the server', () => {
    expect(Object.keys(clientErrors(schema, { signed_on: '1403/13/01' })).sort()).toEqual(['kind', 'signed_on']);
    expect(clientErrors(schema, { kind: 'letter', signed_on: '2024-03-20' })).toEqual({});
  });
});
