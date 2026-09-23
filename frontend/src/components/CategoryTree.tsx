import { List, ListItemButton, ListItemText } from '@mui/material';
import { useMemo } from 'react';
import type { CategoryNode } from '../lib/api';
import { t } from '../strings';

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

  return (
    <List dense component="nav" aria-label={t.categories}>
      <ListItemButton selected={selectedId === null} onClick={() => onSelect(null)}>
        <ListItemText primary={t.allDocuments} />
      </ListItemButton>
      {ordered.map((category) => (
        <ListItemButton
          key={category.id}
          selected={selectedId === category.id}
          onClick={() => onSelect(category.id)}
          // paddingInlineStart follows the reading direction, so nesting indents from the right.
          sx={{ paddingInlineStart: 2 + category.depth * 2 }}
        >
          <ListItemText
            primary={category.name}
            slotProps={{
              primary: {
                noWrap: true,
                color: category.canView ? 'text.primary' : 'text.disabled',
              },
            }}
          />
        </ListItemButton>
      ))}
    </List>
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
