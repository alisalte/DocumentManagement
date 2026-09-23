import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { evaluateRule, parseRule } from './rules';

// The same vectors the C# evaluator is tested against.
const cases = JSON.parse(
  readFileSync(fileURLToPath(new URL('../../../tests/rules/rule-cases.json', import.meta.url)), 'utf8'),
) as {
  evaluate: { name: string; rule: unknown; data: Record<string, unknown>; expected: boolean }[];
  invalid: { name: string; rule: unknown }[];
};

describe('rule language, shared vectors', () => {
  it.each(cases.evaluate)('evaluates: $name', ({ rule, data, expected }) => {
    const parsed = parseRule(rule);
    if ('error' in parsed) throw new Error(parsed.error);
    expect(evaluateRule(parsed.rule, data)).toBe(expected);
  });

  it.each(cases.invalid)('rejects: $name', ({ rule }) => {
    expect('error' in parseRule(rule)).toBe(true);
  });
});
