/**
 * Browser port of the shared rule language (Dms.SharedKernel.Rules). The server stays
 * authoritative; this only decides which fields to show and mark as required while typing.
 * Both implementations are held to tests/rules/rule-cases.json, so keep them in step.
 *
 * One known difference: numbers are IEEE doubles here and decimals on the server, so values past
 * 2^53 can compare differently. That only affects hints in the form, never what is stored.
 */
export type RuleOperator = 'eq' | 'ne' | 'gt' | 'gte' | 'lt' | 'lte' | 'in' | 'not_in' | 'contains' | 'is_empty';

export type Rule =
  | { all: Rule[] }
  | { any: Rule[] }
  | { not: Rule }
  | { field: string; op: RuleOperator; value?: unknown };

type Json = null | boolean | number | string | Json[] | { [key: string]: Json };

export const maxDepth = 8;
export const maxNodes = 100;

const operators = new Set<RuleOperator>(['eq', 'ne', 'gt', 'gte', 'lt', 'lte', 'in', 'not_in', 'contains', 'is_empty']);
const fieldCode = /^[a-z][a-z0-9_]{0,62}$/;

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/** Returns the rule, or an error message in the same cases as RuleParser.Parse. */
export function parseRule(input: unknown): { rule: Rule } | { error: string } {
  let nodes = 0;

  const parse = (element: unknown, depth: number): Rule | string => {
    if (depth > maxDepth) return `nested more than ${maxDepth} levels`;
    if (++nodes > maxNodes) return `more than ${maxNodes} parts`;
    if (!isObject(element)) return 'every part of a rule is an object';

    const keys = Object.keys(element);
    if (keys.length === 1 && (keys[0] === 'all' || keys[0] === 'any')) {
      const list = element[keys[0]];
      if (!Array.isArray(list) || list.length === 0) return `'${keys[0]}' takes a non-empty list`;
      const children: Rule[] = [];
      for (const item of list) {
        const child = parse(item, depth + 1);
        if (typeof child === 'string') return child;
        children.push(child);
      }
      return keys[0] === 'all' ? { all: children } : { any: children };
    }

    if (keys.length === 1 && keys[0] === 'not') {
      const inner = parse(element.not, depth + 1);
      return typeof inner === 'string' ? inner : { not: inner };
    }

    if (!keys.includes('field') || !keys.includes('op') || keys.some((key) => !['field', 'op', 'value'].includes(key))) {
      return "a comparison has exactly 'field', 'op' and 'value'";
    }

    const { field, op } = element;
    if (typeof field !== 'string' || !fieldCode.test(field)) return 'bad field code';
    if (typeof op !== 'string' || !operators.has(op as RuleOperator)) return 'unknown operator';

    const hasValue = 'value' in element;
    const value = element.value;
    if (op === 'is_empty') {
      return hasValue ? "'is_empty' takes no value" : { field, op };
    }

    if (!hasValue) return `'${op}' needs a value`;
    if ((op === 'in' || op === 'not_in') && !Array.isArray(value)) return `'${op}' compares against a list`;
    if (['gt', 'gte', 'lt', 'lte'].includes(op) && typeof value !== 'number' && typeof value !== 'string') {
      return `'${op}' compares against a number or a date`;
    }
    if (op !== 'in' && op !== 'not_in' && (Array.isArray(value) || isObject(value))) {
      return `'${op}' compares against a single value`;
    }

    return { field, op: op as RuleOperator, value };
  };

  const result = parse(input, 1);
  return typeof result === 'string' ? { error: result } : { rule: result };
}

export function evaluateRule(rule: Rule, data: Record<string, unknown>): boolean {
  if ('all' in rule) return rule.all.every((child) => evaluateRule(child, data));
  if ('any' in rule) return rule.any.some((child) => evaluateRule(child, data));
  if ('not' in rule) return !evaluateRule(rule.not, data);

  const raw = data[rule.field];
  const actual = raw === undefined ? null : (raw as Json);

  switch (rule.op) {
    case 'is_empty':
      return isEmpty(actual);
    case 'eq':
      return matches(actual, (element) => scalarEquals(element, rule.value as Json), rule.value === null);
    case 'ne':
      return !matches(actual, (element) => scalarEquals(element, rule.value as Json), rule.value === null);
    case 'gt':
      return compare(actual, rule.value as Json, (sign) => sign > 0);
    case 'gte':
      return compare(actual, rule.value as Json, (sign) => sign >= 0);
    case 'lt':
      return compare(actual, rule.value as Json, (sign) => sign < 0);
    case 'lte':
      return compare(actual, rule.value as Json, (sign) => sign <= 0);
    case 'in':
      return inList(actual, rule.value as Json[]);
    case 'not_in':
      return !inList(actual, rule.value as Json[]);
    case 'contains':
      return contains(actual, rule.value as Json);
  }
}

function isEmpty(value: Json): boolean {
  if (value === null) return true;
  if (typeof value === 'string') return value.trim() === '';
  if (Array.isArray(value)) return value.length === 0;
  return false;
}

function matches(actual: Json, test: (element: Json) => boolean, nullMatches: boolean): boolean {
  if (actual === null) return nullMatches;
  return Array.isArray(actual) ? actual.some(test) : test(actual);
}

function inList(actual: Json, list: Json[]): boolean {
  return list.some((candidate) => matches(actual, (element) => scalarEquals(element, candidate), candidate === null));
}

function contains(actual: Json, expected: Json): boolean {
  if (Array.isArray(actual)) return actual.some((element) => scalarEquals(element, expected));
  if (typeof actual === 'string' && typeof expected === 'string') {
    return actual.toLowerCase().includes(expected.toLowerCase());
  }
  return false;
}

function scalarEquals(left: Json, right: Json): boolean {
  if (typeof left === 'number' && typeof right === 'number') return left === right;
  if (typeof left === 'string' && typeof right === 'string') return left === right;
  if (typeof left === 'boolean' && typeof right === 'boolean') return left === right;
  return left === null && right === null;
}

function compare(actual: Json, expected: Json, test: (sign: number) => boolean): boolean {
  if (typeof actual === 'number' && typeof expected === 'number') {
    return test(Math.sign(actual - expected));
  }

  if (typeof actual === 'string' && typeof expected === 'string') {
    // Ordinal, like string.CompareOrdinal: ISO dates sort correctly this way.
    return test(actual < expected ? -1 : actual > expected ? 1 : 0);
  }

  return false;
}
