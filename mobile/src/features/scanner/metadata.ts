import type { DocumentTypeSchema, FieldSchema, MetadataValue } from '../../types/document';

const simpleTypes = new Set([
  'Text',
  'LongText',
  'Integer',
  'Decimal',
  'Boolean',
  'Date',
  'Url',
  'Email',
  'Phone',
  'Select',
]);

function activeRequired(schema: DocumentTypeSchema): FieldSchema[] {
  return schema.fields.filter((field) => field.isActive && field.isRequired);
}

export function unsupportedRequiredFields(schema: DocumentTypeSchema): FieldSchema[] {
  return activeRequired(schema).filter((field) => !simpleTypes.has(field.type));
}

export function requiredFields(schema: DocumentTypeSchema): FieldSchema[] {
  return activeRequired(schema).filter((field) => simpleTypes.has(field.type));
}

export function metadataErrors(
  fields: FieldSchema[],
  values: Record<string, string>,
): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const field of fields) {
    const raw = values[field.code]?.trim() ?? '';
    if (field.type === 'Boolean') {
      if (raw !== 'true' && raw !== 'false') errors[field.code] = 'این فیلد الزامی است.';
      continue;
    }
    if (!raw) {
      errors[field.code] = 'این فیلد الزامی است.';
      continue;
    }
    if (field.type === 'Integer' && !/^-?\d+$/.test(raw)) {
      errors[field.code] = 'یک عدد صحیح وارد کنید.';
    }
    if (field.type === 'Decimal' && !/^-?\d+(\.\d+)?$/.test(raw)) {
      errors[field.code] = 'یک عدد وارد کنید.';
    }
    if (field.type === 'Date' && !/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
      errors[field.code] = 'تاریخ را به‌صورت YYYY-MM-DD وارد کنید.';
    }
  }
  return errors;
}

export function toMetadata(fields: FieldSchema[], values: Record<string, string>): Record<string, MetadataValue> {
  const metadata: Record<string, MetadataValue> = {};
  for (const field of fields) {
    const raw = values[field.code]?.trim() ?? '';
    if (field.type === 'Boolean') {
      metadata[field.code] = raw === 'true';
    } else if (field.type === 'Integer') {
      metadata[field.code] = Number(raw);
    } else if (field.type === 'Decimal') {
      metadata[field.code] = raw;
    } else if (raw) {
      metadata[field.code] = raw;
    }
  }
  return metadata;
}
