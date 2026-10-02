import type { CategoryNode } from './api';

/**
 * Folders to show as the archive landing grid.
 *
 * Production has a single fixed root («اسناد», parentId null). User folders live
 * under that root, so the dashboard must list those children — not only the root.
 * When several null-parent folders exist (local demo), list those instead.
 */
export function topLevelArchiveFolders(categories: CategoryNode[]): CategoryNode[] {
  const bySort = (a: CategoryNode, b: CategoryNode) =>
    a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'fa');

  const nullParents = categories.filter((category) => category.parentId === null);
  if (nullParents.length === 1) {
    const archiveRoot = nullParents[0]!;
    const children = categories
      .filter((category) => category.parentId === archiveRoot.id && category.canView)
      .sort(bySort);
    if (children.length > 0) return children;
    return archiveRoot.canView ? [archiveRoot] : [];
  }

  return nullParents.filter((category) => category.canView).sort(bySort);
}
