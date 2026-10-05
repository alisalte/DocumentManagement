import type { CategoryNode } from './api';

function bySort(a: CategoryNode, b: CategoryNode): number {
  return a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'fa');
}

/**
 * Folders to show as the archive landing grid.
 *
 * Production has a single fixed root («اسناد», parentId null). User folders live
 * under that root, so the dashboard must list those children — not only the root.
 * When several null-parent folders exist (local demo), list those instead.
 */
export function topLevelArchiveFolders(categories: CategoryNode[]): CategoryNode[] {
  const nullParents = categories.filter((category) => category.parentId === null);
  if (nullParents.length === 1) {
    const archiveRoot = nullParents[0]!;
    const children = visibleChildFolders(categories, archiveRoot.id);
    if (children.length > 0) return children;
    return archiveRoot.canView ? [archiveRoot] : [];
  }

  return nullParents.filter((category) => category.canView).sort(bySort);
}

/** Viewable folders whose parent is `parentId`, in archive order. */
export function visibleChildFolders(categories: CategoryNode[], parentId: string): CategoryNode[] {
  return categories.filter((category) => category.parentId === parentId && category.canView).sort(bySort);
}

/**
 * File totals for folder cards. `directCounts` is documents filed in that folder only;
 * the returned number also includes every descendant folder.
 */
export function subtreeFileCounts(
  categories: ReadonlyArray<Pick<CategoryNode, 'id' | 'parentId'>>,
  directCounts: Readonly<Record<string, number>>,
): Record<string, number> {
  const children = new Map<string, string[]>();
  for (const category of categories) {
    if (!category.parentId) continue;
    const list = children.get(category.parentId);
    if (list) list.push(category.id);
    else children.set(category.parentId, [category.id]);
  }

  const totals: Record<string, number> = {};
  const visiting = new Set<string>();

  const totalOf = (id: string): number => {
    if (id in totals) return totals[id]!;
    if (visiting.has(id)) return directCounts[id] ?? 0;
    visiting.add(id);
    let total = directCounts[id] ?? 0;
    for (const childId of children.get(id) ?? []) total += totalOf(childId);
    visiting.delete(id);
    totals[id] = total;
    return total;
  };

  for (const category of categories) totalOf(category.id);
  return totals;
}
