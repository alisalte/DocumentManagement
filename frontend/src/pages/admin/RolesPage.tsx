import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { EntityPicker } from '../../components/metadata/EntityPicker';
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Chip,
  Dialog,
  IconButton,
  ProgressBar,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  TextArea,
  TextField,
} from '../../components/ui';
import { api, type AdminRole } from '../../lib/api';
import { useSession } from '../../session';
import { describeError } from '../../strings';
import { d, permissionLabel } from './directoryStrings';

/**
 * Roles (ADMIN_MANAGE_ROLES): named sets of system permissions, and who holds them. Nobody can
 * put into a role, or hand out a role with, a permission they do not hold themselves.
 */
export function RolesPage() {
  const roles = useQuery({ queryKey: ['admin-roles'], queryFn: () => api.admin.roles() });
  const [creating, setCreating] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const role = roles.data?.find((candidate) => candidate.id === selected) ?? null;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{d.roles}</h1>
        <Button onClick={() => setCreating(true)}>{d.newRole}</Button>
      </div>

      <Card flush>
        {roles.isPending && <ProgressBar className="rounded-t-xl" />}
        {roles.isError && (
          <div className="p-4">
            <Alert severity="error">{describeError(roles.error)}</Alert>
          </div>
        )}
        {roles.data && roles.data.length > 0 && (
          <Table dense>
            <THead>
              <TR>
                <TH>{d.name}</TH>
                <TH>{d.code}</TH>
                <TH />
              </TR>
            </THead>
            <TBody>
              {roles.data.map((item) => (
                <TR
                  key={item.id}
                  role="button"
                  tabIndex={0}
                  className="cursor-pointer"
                  onClick={() => setSelected(item.id)}
                  onKeyDown={(event) => event.key === 'Enter' && setSelected(item.id)}
                >
                  <TD>
                    <span className="flex flex-wrap items-center gap-1.5">
                      <span className="font-semibold text-ink-800">{item.name}</span>
                      {item.isSystem && <Chip size="small" variant="outlined" label={d.systemRole} />}
                    </span>
                  </TD>
                  <TD>
                    <span dir="ltr" className="text-paper-500">
                      {item.code}
                    </span>
                  </TD>
                  <TD className="text-end whitespace-nowrap">
                    <span className="text-paper-500">
                      {item.permissions.length.toLocaleString('fa-IR')} {d.permission}
                    </span>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {!roles.isPending && !roles.isError && (roles.data?.length ?? 0) === 0 && (
          <p className="py-10 text-center text-sm text-paper-500">{d.empty}</p>
        )}
      </Card>

      <CreateRoleDialog open={creating} onClose={() => setCreating(false)} onCreated={setSelected} />
      {role && <RoleDialog role={role} onClose={() => setSelected(null)} />}
    </div>
  );
}

function CreateRoleDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: string) => void }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({ code: '', name: '', description: '' });
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const { id } = await api.admin.createRole({ code: form.code.trim(), name: form.name.trim(), description: form.description.trim() || null });
      await queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
      setForm({ code: '', name: '', description: '' });
      onClose();
      onCreated(id);
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={d.newRole}
      maxWidth="md"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {d.cancel}
          </Button>
          <Button type="submit" form="admin-create-role">
            {d.create}
          </Button>
        </>
      }
    >
      <form id="admin-create-role" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <TextField
          label={d.code}
          value={form.code}
          onChange={(event) => setForm({ ...form, code: event.target.value })}
          helperText={d.codeHelp}
          required
          dir="ltr"
        />
        <TextField label={d.name} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required />
        <TextArea
          label={d.description}
          value={form.description}
          onChange={(event) => setForm({ ...form, description: event.target.value })}
          rows={2}
          className="sm:col-span-2"
        />
        {error && (
          <Alert severity="error" className="sm:col-span-2">
            {error}
          </Alert>
        )}
      </form>
    </Dialog>
  );
}

function RoleDialog({ role, onClose }: { role: AdminRole; onClose: () => void }) {
  const { user } = useSession();
  const queryClient = useQueryClient();
  const catalog = useQuery({
    queryKey: ['permission-catalog'],
    queryFn: api.acl.catalog,
    staleTime: Infinity,
  });
  const holders = useQuery({ queryKey: ['admin-role-users', role.id], queryFn: () => api.admin.roleUsers(role.id) });
  const [name, setName] = useState(role.name);
  const [description, setDescription] = useState(role.description ?? '');
  const [codes, setCodes] = useState<string[]>(role.permissions);
  const [adding, setAdding] = useState<string | null>(null);
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    setName(role.name);
    setDescription(role.description ?? '');
    setCodes(role.permissions);
  }, [role]);

  const systemPermissions = (catalog.data ?? []).filter((definition) => definition.scope !== 'Resource');
  // What the caller may hand out; the server enforces the same rule.
  const mayGrant = (code: string) => !!user && (user.isSystemAdmin || user.systemPermissions.includes(code) || role.permissions.includes(code));

  const run = async (work: () => Promise<unknown>, done?: string) => {
    setMessage(null);
    try {
      await work();
      if (done) setMessage({ severity: 'success', text: done });
      await queryClient.invalidateQueries({ queryKey: ['admin-roles'] });
      await queryClient.invalidateQueries({ queryKey: ['admin-role-users', role.id] });
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    }
  };

  const save = () =>
    run(async () => {
      await api.admin.updateRole(role.id, { name: name.trim(), description: description.trim() || null });
      await api.admin.setRolePermissions(role.id, codes);
    }, d.saved);

  return (
    <Dialog
      open
      onClose={onClose}
      title={role.name}
      maxWidth="lg"
      footer={
        <Button variant="ghost" onClick={onClose}>
          {d.close}
        </Button>
      }
    >
      <div className="space-y-5">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label={d.name} value={name} onChange={(event) => setName(event.target.value)} required />
          <TextArea label={d.description} value={description} onChange={(event) => setDescription(event.target.value)} rows={2} />
        </div>

        <div className="space-y-2">
          <div>
            <h2 className="text-sm font-semibold text-ink-800">{d.rolePermissions}</h2>
            <p className="mt-1 text-sm text-paper-500">{d.rolePermissionsHelp}</p>
          </div>
          <div className="grid gap-2 sm:grid-cols-2">
            {systemPermissions.map((definition) => (
              <Checkbox
                key={definition.code}
                checked={codes.includes(definition.code)}
                disabled={!mayGrant(definition.code)}
                onChange={(event) =>
                  setCodes(event.target.checked ? [...codes, definition.code] : codes.filter((code) => code !== definition.code))
                }
                label={
                  <span>
                    {permissionLabel(definition.code)}{' '}
                    <span dir="ltr" className="text-xs text-paper-400">
                      {definition.code}
                    </span>
                  </span>
                }
              />
            ))}
          </div>
          <div className="flex flex-wrap gap-2">
            <Button onClick={save}>{d.save}</Button>
          </div>
        </div>

        <div className="space-y-2">
          <h2 className="text-sm font-semibold text-ink-800">{d.holders}</h2>
          {holders.isPending && <ProgressBar className="rounded-full" />}
          <div className="space-y-1">
            {holders.isSuccess && holders.data.length === 0 && (
              <p className="py-6 text-center text-sm text-paper-500">{d.empty}</p>
            )}
            {(holders.data ?? []).map((holder) => (
              <div key={holder.id} className="flex items-center gap-2 rounded-lg px-2 py-1.5 transition-colors hover:bg-paper-50">
                <span className={`min-w-0 flex-1 text-sm ${holder.isActive ? '' : 'opacity-60'}`}>{holder.displayName}</span>
                <IconButton label={d.remove} size="sm" onClick={() => run(() => api.admin.unassignRole(role.id, holder.id))}>
                  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5">
                    <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
                  </svg>
                </IconButton>
              </div>
            ))}
          </div>

          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <div className="min-w-0 flex-1">
              <EntityPicker kind="User" label={d.addHolder} value={adding} onChange={setAdding} />
            </div>
            <Button disabled={!adding} onClick={() => adding && run(() => api.admin.assignRole(role.id, adding)).then(() => setAdding(null))}>
              {d.add}
            </Button>
          </div>
        </div>

        {message && <Alert severity={message.severity}>{message.text}</Alert>}
      </div>
    </Dialog>
  );
}
