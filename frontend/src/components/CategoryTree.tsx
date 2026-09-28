import { useEffect, useMemo, useState } from 'react';
import type { CategoryNode } from '../lib/api';
import { t } from '../strings';
import { cx } from './ui';

interface Props {
  categories: CategoryNode[];
  selectedId: string | null;
  onSelect: (categoryId: string | null) => void;
}

interface TreeNode extends CategoryNode {
  children: TreeNode[];
}

/**
 * Folder browser with a visible tree: guide lines, folder icons, and expand/collapse.
 * Only folders the user may browse are interactive; ancestors on the path stay visible but muted.
 */
export function CategoryTree({ categories, selectedId, onSelect }: Props) {
  const roots = useMemo(() => buildTree(categories), [categories]);
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());

  // Keep the path to the selected folder open when the selection or tree changes.
  useEffect(() => {
    if (!selectedId) return;
    const path = ancestorsOf(categories, selectedId);
    if (path.length === 0) return;
    setExpanded((current) => {
      const next = new Set(current);
      for (const id of path) next.add(id);
      return next;
    });
  }, [categories, selectedId]);

  // First visit: open roots that have children so the hierarchy is visible immediately.
  useEffect(() => {
    setExpanded((current) => {
      if (current.size > 0) return current;
      const next = new Set<string>();
      for (const root of roots) {
        if (root.children.length > 0) next.add(root.id);
      }
      return next;
    });
  }, [roots]);

  const toggle = (id: string) => {
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return (
    <nav aria-label={t.categories} className="space-y-0.5">
      <button
        type="button"
        onClick={() => onSelect(null)}
        className={rowClasses(selectedId === null, false)}
      >
        <span className="grid size-7 shrink-0 place-items-center rounded-lg bg-ink-50 text-ink-700">
          <AllDocsIcon />
        </span>
        <span className="truncate">{t.allDocuments}</span>
      </button>

      <ul className="mt-1 space-y-0">
        {roots.map((node, index) => (
          <TreeRow
            key={node.id}
            node={node}
            depth={0}
            isLast={index === roots.length - 1}
            selectedId={selectedId}
            expanded={expanded}
            onToggle={toggle}
            onSelect={onSelect}
            guides={[]}
          />
        ))}
      </ul>
    </nav>
  );
}

function TreeRow({
  node,
  depth,
  isLast,
  selectedId,
  expanded,
  onToggle,
  onSelect,
  guides,
}: {
  node: TreeNode;
  depth: number;
  isLast: boolean;
  selectedId: string | null;
  expanded: Set<string>;
  onToggle: (id: string) => void;
  onSelect: (categoryId: string | null) => void;
  /** For each ancestor level: whether that ancestor still has siblings below (draw a continuing guide). */
  guides: boolean[];
}) {
  const hasChildren = node.children.length > 0;
  const open = expanded.has(node.id);
  const selected = selectedId === node.id;
  const muted = !node.canView;

  return (
    <li className="relative">
      <div className="flex min-h-9 items-stretch">
        {/* Tree guides: vertical stems + elbow into this row (RTL: drawn from the start edge). */}
        <div className="flex shrink-0" aria-hidden>
          {guides.map((continueDown, level) => (
            <span key={level} className="relative w-4">
              {continueDown && (
                <span className="absolute inset-y-0 start-1/2 w-px -translate-x-1/2 bg-paper-300" />
              )}
            </span>
          ))}
          {depth > 0 && (
            <span className="relative w-4">
              {!isLast && <span className="absolute inset-y-0 start-1/2 w-px -translate-x-1/2 bg-paper-300" />}
              {isLast && <span className="absolute top-0 bottom-1/2 start-1/2 w-px -translate-x-1/2 bg-paper-300" />}
              <span className="absolute top-1/2 start-1/2 end-0 h-px bg-paper-300" />
            </span>
          )}
        </div>

        <div className="flex min-w-0 flex-1 items-center gap-0.5 py-0.5 pe-0.5">
          {hasChildren ? (
            <button
              type="button"
              aria-expanded={open}
              aria-label={open ? 'بستن' : 'باز کردن'}
              onClick={(event) => {
                event.stopPropagation();
                onToggle(node.id);
              }}
              className="inline-flex size-7 shrink-0 items-center justify-center rounded-lg text-paper-500 transition-colors hover:bg-ink-50 hover:text-ink-800"
            >
              <ChevronIcon open={open} />
            </button>
          ) : (
            <span className="size-7 shrink-0" aria-hidden />
          )}

          <button
            type="button"
            onClick={() => onSelect(node.id)}
            title={node.name}
            className={cx(rowClasses(selected, muted), 'min-w-0 flex-1')}
          >
            <span
              className={cx(
                'grid size-7 shrink-0 place-items-center rounded-lg',
                selected ? 'bg-ink-200/80 text-ink-800' : 'bg-paper-100 text-paper-600',
                open && hasChildren && !selected && 'bg-ink-50 text-ink-600',
              )}
            >
              <FolderIcon open={open && hasChildren} />
            </span>
            <span className="truncate">{node.name}</span>
          </button>
        </div>
      </div>

      {hasChildren && open && (
        <ul>
          {node.children.map((child, index) => (
            <TreeRow
              key={child.id}
              node={child}
              depth={depth + 1}
              isLast={index === node.children.length - 1}
              selectedId={selectedId}
              expanded={expanded}
              onToggle={onToggle}
              onSelect={onSelect}
              guides={[...guides, !isLast]}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

const rowClasses = (selected: boolean, muted: boolean) =>
  cx(
    'flex w-full items-center gap-2 rounded-xl px-2 py-1.5 text-start text-sm transition-all duration-150',
    'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ink-600',
    selected
      ? 'bg-ink-100/90 font-semibold text-ink-800 shadow-[inset_-3px_0_0_0_rgb(30_74_117)]'
      : 'text-ink-800 hover:bg-ink-50',
    muted && !selected && 'text-paper-400',
  );

function buildTree(categories: CategoryNode[]): TreeNode[] {
  const byId = new Map<string, TreeNode>();
  for (const category of categories) {
    byId.set(category.id, { ...category, children: [] });
  }

  const roots: TreeNode[] = [];
  for (const category of categories) {
    const node = byId.get(category.id)!;
    const parent = category.parentId ? byId.get(category.parentId) : undefined;
    if (parent) parent.children.push(node);
    else roots.push(node);
  }

  const sortNodes = (nodes: TreeNode[]) => {
    nodes.sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'fa'));
    nodes.forEach((node) => sortNodes(node.children));
  };
  sortNodes(roots);
  return roots;
}

function ancestorsOf(categories: CategoryNode[], id: string): string[] {
  const byId = new Map(categories.map((category) => [category.id, category]));
  const path: string[] = [];
  let current = byId.get(id);
  while (current?.parentId) {
    path.push(current.parentId);
    current = byId.get(current.parentId);
  }
  return path;
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg
      viewBox="0 0 20 20"
      fill="currentColor"
      aria-hidden
      className={cx(
        'size-3.5 transition-transform duration-150',
        // Down when open; when closed, point toward the start edge (RTL-aware).
        open ? 'rotate-0' : 'ltr:-rotate-90 rtl:rotate-90',
      )}
    >
      <path
        fillRule="evenodd"
        d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z"
        clipRule="evenodd"
      />
    </svg>
  );
}

function FolderIcon({ open }: { open: boolean }) {
  if (open) {
    return (
      <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden className="size-3.5">
        <path d="M19.906 9c.382 0 .749.057 1.094.162V9a3 3 0 0 0-3-3h-3.879a.75.75 0 0 1-.53-.22L11.47 3.66A2.25 2.25 0 0 0 9.879 3H6a3 3 0 0 0-3 3v3.162A3.735 3.735 0 0 1 4.094 9h15.812ZM4.094 10.5a2.25 2.25 0 0 0-2.227 2.568l.857 6A2.25 2.25 0 0 0 4.951 21h14.098a2.25 2.25 0 0 0 2.227-1.932l.857-6a2.25 2.25 0 0 0-2.227-2.568H4.094Z" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden className="size-3.5">
      <path d="M19.5 21a3 3 0 0 0 3-3v-9a3 3 0 0 0-3-3h-5.379a.75.75 0 0 1-.53-.22L11.47 3.66A2.25 2.25 0 0 0 9.879 3H4.5a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3h15Z" />
    </svg>
  );
}

function AllDocsIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6} aria-hidden className="size-3.5">
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M3.75 6A2.25 2.25 0 0 1 6 3.75h1.5A2.25 2.25 0 0 1 9.75 6v1.5A2.25 2.25 0 0 1 7.5 9.75H6A2.25 2.25 0 0 1 3.75 7.5V6ZM3.75 15.75A2.25 2.25 0 0 1 6 13.5h1.5a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 7.5 20.25H6A2.25 2.25 0 0 1 3.75 18v-2.25ZM13.5 6a2.25 2.25 0 0 1 2.25-2.25H17A2.25 2.25 0 0 1 19.25 6v1.5A2.25 2.25 0 0 1 17 9.75h-1.25A2.25 2.25 0 0 1 13.5 7.5V6ZM13.5 15.75a2.25 2.25 0 0 1 2.25-2.25H17a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 17 20.25h-1.25A2.25 2.25 0 0 1 13.5 18v-2.25Z"
      />
    </svg>
  );
}
