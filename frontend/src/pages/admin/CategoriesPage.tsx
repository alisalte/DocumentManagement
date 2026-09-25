import type { CSSProperties } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { AclEditor } from '../../components/acl/AclEditor';
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Chip,
  Dialog,
  ProgressBar,
  Select,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  TextArea,
  TextField,
} from '../../components/ui';
import { api, type CategoryNode } from '../../lib/api';
import { describeError } from '../../strings';
import { d } from './directoryStrings';

/** Depth-first, siblings by sort order then name: the tree as the browser shows it. */
export function orderTree(categories: CategoryNode[]): CategoryNode[] {
  const children = new Map<string | null, CategoryNode[]>();
  for (const category of categories) {
    const siblings = children.get(category.parentId) ?? [];
    siblings.push(category);
    children.set(category.parentId, siblings);
  }

  const ids = new Set(categories.map((category) => category.id));
  const ordered: CategoryNode[] = [];
  const visit = (parentId: string | null) => {
    for (const category of (children.get(parentId) ?? []).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'fa'))) {
      ordered.push(category);
      visit(category.id);
    }
  };

  visit(null);
  // Categories whose parent is not visible still appear, at the top.
  for (const category of categories) {
    if (category.parentId && !ids.has(category.parentId) && !ordered.includes(category)) {
      ordered.push(category);
      visit(category.id);
    }
  }

  return ordered;
}

/** Everything below a category, which it cannot be moved into. */
function descendantsOf(categories: CategoryNode[], id: string): Set<string> {
  const result = new Set<string>([id]);
  let grew = true;
  while (grew) {
    grew = false;
    for (const category of categories) {
      if (category.parentId && result.has(category.parentId) && !result.has(category.id)) {
        result.add(category.id);
        grew = true;
      }
    }
  }

  return result;
}

type Editing =
  | { kind: 'create'; parent: CategoryNode | null }
  | { kind: 'edit'; category: CategoryNode }
  | { kind: 'move'; category: CategoryNode }
  | { kind: 'acl'; category: CategoryNode };

/** The category tree (ADMIN_MANAGE_CATEGORIES), and the permissions of each category. */
export function CategoriesPage() {
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  const [editing, setEditing] = useState<Editing | null>(null);
  const all = categories.data ?? [];
  const ordered = orderTree(all);
  const minDepth = Math.min(...all.map((category) => category.depth), 0);

  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-bold text-slate-800">{d.categories}</h1>
        <Button onClick={() => setEditing({ kind: 'create', parent: null })}>{d.newCategory}</Button>
      </div>

      <Card flush>
        {categories.isPending && <ProgressBar className="rounded-t-xl" />}
        {categories.isError && (
          <div className="p-4">
            <Alert severity="error">{describeError(categories.error)}</Alert>
          </div>
        )}
        {ordered.length > 0 && (
          <Table dense>
            <THead>
              <TR>
                <TH>{d.name}</TH>
                <TH>{d.code}</TH>
                <TH />
              </TR>
            </THead>
            <TBody>
              {ordered.map((category) => (
                <TR key={category.id}>
                  <TD
                    className="ps-[calc(1rem_+_var(--depth)*12px)] sm:ps-[calc(1rem_+_var(--depth)*24px)]"
                    style={{ '--depth': category.depth - minDepth } as CSSProperties}
                  >
                    <span className="flex flex-wrap items-center gap-1.5">
                      <span className="font-semibold text-slate-800">{category.name}</span>
                      {!category.isActive && <Chip size="small" label={d.inactive} />}
                    </span>
                  </TD>
                  <TD>
                    <span dir="ltr" className="text-slate-500">
                      {category.code}
                    </span>
                  </TD>
                  <TD className="text-end whitespace-nowrap">
                    <span className="inline-flex flex-wrap items-center justify-end gap-1">
                      <Button size="sm" variant="ghost" onClick={() => setEditing({ kind: 'create', parent: category })}>
                        {d.newSubcategory}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setEditing({ kind: 'edit', category })}>
                        {d.edit}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setEditing({ kind: 'move', category })}>
                        {d.move}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setEditing({ kind: 'acl', category })}>
                        {d.permissionsTitle}
                      </Button>
                    </span>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {!categories.isPending && !categories.isError && ordered.length === 0 && (
          <p className="py-10 text-center text-sm text-slate-500">{d.empty}</p>
        )}
      </Card>

      {editing?.kind === 'create' && <CategoryForm parent={editing.parent} onClose={() => setEditing(null)} />}
      {editing?.kind === 'edit' && <CategoryForm category={editing.category} onClose={() => setEditing(null)} />}
      {editing?.kind === 'move' && <MoveDialog category={editing.category} all={ordered} onClose={() => setEditing(null)} />}
      {editing?.kind === 'acl' && (
        <AclEditor resourceType="Category" resourceId={editing.category.id} resourceName={editing.category.name} open onClose={() => setEditing(null)} />
      )}
    </div>
  );
}

function CategoryForm({ parent, category, onClose }: { parent?: CategoryNode | null; category?: CategoryNode; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    name: category?.name ?? '',
    code: category?.code ?? '',
    description: category?.description ?? '',
    isActive: category?.isActive ?? true,
    sortOrder: category?.sortOrder ?? 0,
  });
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      if (category) {
        await api.admin.updateCategory(category.id, {
          name: form.name.trim(),
          description: form.description.trim() || null,
          isActive: form.isActive,
          sortOrder: Number(form.sortOrder) || 0,
        });
      } else {
        await api.admin.createCategory({
          parentId: parent?.id ?? null,
          name: form.name.trim(),
          code: form.code.trim(),
          description: form.description.trim() || null,
        });
      }

      await queryClient.invalidateQueries({ queryKey: ['categories'] });
      onClose();
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={category ? category.name : parent ? `${d.newSubcategory}: ${parent.name}` : d.newCategory}
      maxWidth="md"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {d.cancel}
          </Button>
          <Button type="submit" form="admin-category-form">
            {category ? d.save : d.create}
          </Button>
        </>
      }
    >
      <form id="admin-category-form" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <TextField label={d.name} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required />
        <TextField
          label={d.code}
          value={form.code}
          onChange={(event) => setForm({ ...form, code: event.target.value })}
          helperText={d.codeHelp}
          disabled={!!category}
          required
          dir="ltr"
        />
        <TextArea
          label={d.description}
          value={form.description}
          onChange={(event) => setForm({ ...form, description: event.target.value })}
          rows={2}
          className="sm:col-span-2"
        />
        {category && (
          <>
            <TextField
              label={d.sortOrder}
              type="number"
              value={form.sortOrder}
              onChange={(event) => setForm({ ...form, sortOrder: Number(event.target.value) })}
            />
            <div className="flex items-end pb-2.5">
              <Checkbox label={d.active} checked={form.isActive} onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />
            </div>
            {!form.isActive && (
              <Alert severity="info" className="sm:col-span-2">
                {d.categoryInactiveHelp}
              </Alert>
            )}
          </>
        )}
        {error && (
          <Alert severity="error" className="sm:col-span-2">
            {error}
          </Alert>
        )}
      </form>
    </Dialog>
  );
}

function MoveDialog({ category, all, onClose }: { category: CategoryNode; all: CategoryNode[]; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<string>(category.parentId ?? '');
  const [error, setError] = useState<string | null>(null);
  const excluded = descendantsOf(all, category.id);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      await api.admin.moveCategory(category.id, target || null);
      await queryClient.invalidateQueries({ queryKey: ['categories'] });
      onClose();
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={`${d.move}: ${category.name}`}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {d.cancel}
          </Button>
          <Button type="submit" form="admin-move-category" disabled={target === (category.parentId ?? '')}>
            {d.move}
          </Button>
        </>
      }
    >
      <form id="admin-move-category" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <Select label={d.moveTo} value={target} onChange={(event) => setTarget(event.target.value)} className="sm:col-span-2">
          <option value="">{d.root}</option>
          {all
            .filter((candidate) => !excluded.has(candidate.id))
            .map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {'\u00A0'.repeat(candidate.depth * 4)}
                {candidate.name}
              </option>
            ))}
        </Select>
        {error && (
          <Alert severity="error" className="sm:col-span-2">
            {error}
          </Alert>
        )}
      </form>
    </Dialog>
  );
}
