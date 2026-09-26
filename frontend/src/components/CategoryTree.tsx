import { useMemo } from 'react';
import type { CategoryNode } from '../lib/api';
import { t } from '../strings';
import { cx } from './ui';

interface Props {
  categories: CategoryNode[];
  selectedId: string | null;
  onSelect: (categoryId: string | null) => void;
}

/**
 * The category tree as the server returned it: only folders the user may browse, plus the
 * folders on the way to them (shown dimmed, since they only exist for navigation).
 */
export function CategoryTree({ categories, selectedId, onSelect }: Props) {
  const ordered = useMemo(() => flatten(categories), [categories]);

  const rowClasses = (selected: boolean, muted: boolean) =>
    cx(
      'flex w-full items-center gap-2 rounded-xl px-2.5 py-2 text-start text-sm transition-all duration-150',
      'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ink-600',
      selected
        ? 'bg-ink-100/90 font-semibold text-ink-800 shadow-[inset_-3px_0_0_0_rgb(30_74_117)]'
        : 'text-ink-800 hover:bg-ink-50',
      muted && !selected && 'text-paper-400',
    );

  return (
    <nav aria-label={t.categories} className="space-y-0.5">
      <button type="button" onClick={() => onSelect(null)} className={rowClasses(selectedId === null, false)}>
        {t.allDocuments}
      </button>
      {ordered.map((category) => (
        <button
          key={category.id}
          type="button"
          onClick={() => onSelect(category.id)}
          // paddingInlineStart follows the reading direction, so nesting indents from the right.
          style={{ paddingInlineStart: 0.625 + category.depth * 0.875 + 'rem' }}
          className={rowClasses(selectedId === category.id, !category.canView)}
          title={category.name}
        >
          <span className="truncate">{category.name}</span>
        </button>
      ))}
    </nav>
  );
}

/** Depth-first order, so every folder sits directly under its parent. */
function flatten(categories: CategoryNode[]): CategoryNode[] {
  const children = new Map<string | null, CategoryNode[]>();
  for (const category of categories) {
    const siblings = children.get(category.parentId) ?? [];
    siblings.push(category);
    children.set(category.parentId, siblings);
  }

  const ids = new Set(categories.map((category) => category.id));
  const roots = categories.filter((category) => category.parentId === null || !ids.has(category.parentId));
  const result: CategoryNode[] = [];

  const visit = (node: CategoryNode) => {
    result.push(node);
    const kids = (children.get(node.id) ?? []).sort(
      (a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'fa'),
    );
    kids.forEach(visit);
  };

  roots.forEach(visit);
  return result;
}
