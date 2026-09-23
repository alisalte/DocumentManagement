import type { DocumentTypeSchema, FieldSchema, Metadata } from './api';
import { normalizeDigits } from './dates';
import { evaluateRule, parseRule, type Rule } from './rules';

/**
 * Client-side form state for dynamic metadata: which fields to show and mark as required while the
 * user types. It mirrors MetadataValidator on the server (hidden fields resolved to a fixed point,
 * hidden values dropped) but is advisory only; the server's answer is the one that counts.
 */
export interface FormState {
  visible: Set<string>;
  required: Set<string>;
}

const parsed = new WeakMap<object, Rule | null>();

function ruleOf(expression: unknown): Rule | null {
  if (!expression || typeof expression !== 'object') return null;
  if (!parsed.has(expression)) {
    const result = parseRule(expression);
    parsed.set(expression, 'rule' in result ? result.rule : null);
  }
  return parsed.get(expression) ?? null;
}

/** Values as the rule language sees them: numbers as numbers, blanks as missing. */
export function ruleData(schema: DocumentTypeSchema, values: Metadata): Record<string, unknown> {
  const data: Record<string, unknown> = {};
  for (const field of schema.fields) {
    const value = values[field.code];
    if (value === undefined || value === null || value === '') continue;
    if ((field.type === 'Integer' || field.type === 'Decimal') && typeof value === 'string') {
      const number = Number(normalizeDigits(value).replace(/[,٬]/g, '').replace('٫', '.'));
      data[field.code] = Number.isFinite(number) ? number : value;
    } else {
      data[field.code] = value;
    }
  }
  return data;
}

export function evaluateForm(schema: DocumentTypeSchema, values: Metadata): FormState {
  const data = ruleData(schema, values);
  const showRules = schema.rules.filter((rule) => rule.kind === 'Show' && ruleOf(rule.condition));
  let hidden = new Set<string>();

  for (let round = 0; round <= schema.fields.length; round++) {
    const visibleData = Object.fromEntries(Object.entries(data).filter(([code]) => !hidden.has(code)));
    const next = new Set(hidden);
    for (const rule of showRules) {
      if (!evaluateRule(ruleOf(rule.condition)!, visibleData)) rule.targets.forEach((target) => next.add(target));
    }
    if (next.size === hidden.size) break;
    hidden = next;
  }

  const visibleData = Object.fromEntries(Object.entries(data).filter(([code]) => !hidden.has(code)));
  const visible = new Set(schema.fields.filter((field) => field.isActive && !hidden.has(field.code)).map((field) => field.code));
  const required = new Set<string>();

  for (const field of schema.fields) {
    if (!visible.has(field.code)) continue;
    const byRule = schema.rules.some(
      (rule) =>
        rule.kind === 'Require' &&
        rule.targets.includes(field.code) &&
        (!rule.condition || (ruleOf(rule.condition) && evaluateRule(ruleOf(rule.condition)!, visibleData))),
    );
    if (field.isRequired || byRule) required.add(field.code);
  }

  return { visible, required };
}

/** What gets sent: visible fields only, blanks dropped, so nothing hidden is ever submitted. */
export function toSubmission(schema: DocumentTypeSchema, values: Metadata): Metadata {
  const { visible } = evaluateForm(schema, values);
  const result: Metadata = {};
  for (const field of schema.fields) {
    const value = values[field.code];
    if (!visible.has(field.code) || value === undefined || value === null || value === '') continue;
    if (Array.isArray(value) && value.length === 0) continue;
    result[field.code] = value;
  }
  return result;
}

/** Starting values for a new document: the schema defaults. */
export function defaultsOf(schema: DocumentTypeSchema): Metadata {
  const values: Metadata = {};
  for (const field of schema.fields) {
    if (field.isActive && field.defaultValue !== undefined && field.defaultValue !== null) {
      values[field.code] = field.defaultValue;
    }
  }
  return values;
}

/** Checks the browser can make before asking the server: required fields and parseable dates. */
export function clientErrors(schema: DocumentTypeSchema, values: Metadata): Record<string, string[]> {
  const { visible, required } = evaluateForm(schema, values);
  const errors: Record<string, string[]> = {};
  for (const field of schema.fields) {
    if (!visible.has(field.code)) continue;
    const value = values[field.code];
    const empty = value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0);
    if (empty && required.has(field.code)) {
      errors[field.code] = ['این فیلد الزامی است.'];
    } else if (!empty && field.type === 'Date' && !/^\d{4}-\d{2}-\d{2}$/.test(String(value))) {
      errors[field.code] = ['تاریخ را به شکل ۱۴۰۳/۰۱/۳۱ وارد کنید.'];
    }
  }
  return errors;
}

export function orderedFields(schema: DocumentTypeSchema): FieldSchema[] {
  return [...schema.fields].sort((a, b) => a.displayOrder - b.displayOrder || a.code.localeCompare(b.code));
}
