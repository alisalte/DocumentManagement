import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { EntityPicker } from '../../components/metadata/EntityPicker';
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Chip,
  Dialog,
  ProgressBar,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  TextField,
} from '../../components/ui';
import { api, type AdminUser } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { useSession } from '../../session';
import { describeError } from '../../strings';
import { d } from './directoryStrings';

const pageSize = 50;

/** People (ADMIN_MANAGE_USERS). Making or changing a system administrator takes one. */
export function UsersPage() {
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [creating, setCreating] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(search.trim()), 300);
    return () => clearTimeout(handle);
  }, [search]);

  const users = useInfiniteQuery({
    queryKey: ['admin-users', debounced],
    queryFn: ({ pageParam }) => api.admin.users({ search: debounced || undefined, skip: pageParam, take: pageSize }),
    initialPageParam: 0,
    getNextPageParam: (last, all) => (last.length === pageSize ? all.length * pageSize : undefined),
  });

  const rows = users.data?.pages.flat() ?? [];
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{d.users}</h1>
        <Button onClick={() => setCreating(true)}>{d.newUser}</Button>
      </div>

      <div className="sm:max-w-md">
        <TextField label={d.search} value={search} onChange={(event) => setSearch(event.target.value)} />
      </div>

      <Card flush>
        {users.isPending && <ProgressBar className="rounded-t-xl" />}
        {users.isError && (
          <div className="p-4">
            <Alert severity="error">{describeError(users.error)}</Alert>
          </div>
        )}
        {rows.length > 0 && (
          <Table dense>
            <THead>
              <TR>
                <TH>{d.displayName}</TH>
                <TH>{d.username}</TH>
                <TH />
              </TR>
            </THead>
            <TBody>
              {rows.map((user) => (
                <UserRow key={user.id} user={user} onOpen={() => setSelected(user.id)} />
              ))}
            </TBody>
          </Table>
        )}
        {!users.isPending && !users.isError && rows.length === 0 && (
          <p className="py-10 text-center text-sm text-paper-500">{d.empty}</p>
        )}
        {users.hasNextPage && (
          <div className="flex justify-center border-t border-paper-100 p-3">
            <Button onClick={() => users.fetchNextPage()} disabled={users.isFetchingNextPage}>
              {d.more}
            </Button>
          </div>
        )}
      </Card>

      <CreateUserDialog open={creating} onClose={() => setCreating(false)} onCreated={(id) => setSelected(id)} />
      {selected && <UserDialog userId={selected} onClose={() => setSelected(null)} />}
    </div>
  );
}

function UserRow({ user, onOpen }: { user: AdminUser; onOpen: () => void }) {
  return (
    <TR
      role="button"
      tabIndex={0}
      className="cursor-pointer"
      onClick={onOpen}
      onKeyDown={(event) => event.key === 'Enter' && onOpen()}
    >
      <TD>
        <span className="font-semibold text-ink-800">{user.displayName}</span>
      </TD>
      <TD>
        <span dir="ltr" className="text-paper-500">
          {user.username}
        </span>
      </TD>
      <TD className="text-end whitespace-nowrap">
        <span className="inline-flex flex-wrap items-center justify-end gap-1.5">
          {user.isSystemAdmin && <Chip size="small" color="secondary" label={d.systemAdmin} />}
          {!user.isActive && <Chip size="small" label={d.inactive} />}
          {user.mustChangePassword && <Chip size="small" variant="outlined" label={d.mustChangePassword} />}
        </span>
      </TD>
    </TR>
  );
}

function CreateUserDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: string) => void }) {
  const { user: me } = useSession();
  const queryClient = useQueryClient();
  const [form, setForm] = useState({ username: '', displayName: '', email: '', password: '', isSystemAdmin: false });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const { id } = await api.admin.createUser({
        username: form.username.trim(),
        displayName: form.displayName.trim(),
        email: form.email.trim() || null,
        password: form.password,
        isSystemAdmin: form.isSystemAdmin,
        mustChangePassword: true,
      });
      await queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setForm({ username: '', displayName: '', email: '', password: '', isSystemAdmin: false });
      onClose();
      onCreated(id);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={d.newUser}
      maxWidth="md"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {d.cancel}
          </Button>
          <Button type="submit" form="admin-create-user" disabled={busy}>
            {d.create}
          </Button>
        </>
      }
    >
      <form id="admin-create-user" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <TextField
          label={d.username}
          value={form.username}
          onChange={(event) => setForm({ ...form, username: event.target.value })}
          required
          dir="ltr"
          autoComplete="off"
        />
        <TextField label={d.displayName} value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
        <TextField label={d.email} type="email" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} dir="ltr" />
        <TextField
          label={d.password}
          type="password"
          value={form.password}
          onChange={(event) => setForm({ ...form, password: event.target.value })}
          helperText={d.passwordHelp}
          required
          autoComplete="new-password"
        />
        {me?.isSystemAdmin && (
          <div className="sm:col-span-2">
            <Checkbox
              label={d.systemAdmin}
              checked={form.isSystemAdmin}
              onChange={(event) => setForm({ ...form, isSystemAdmin: event.target.checked })}
            />
          </div>
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

function UserDialog({ userId, onClose }: { userId: string; onClose: () => void }) {
  const { user: me } = useSession();
  const queryClient = useQueryClient();
  const details = useQuery({ queryKey: ['admin-user', userId], queryFn: () => api.admin.user(userId) });
  const roles = useQuery({ queryKey: ['admin-user-roles', userId], queryFn: () => api.admin.roles(userId), retry: false });
  const [profile, setProfile] = useState({ displayName: '', email: '' });
  const [newPassword, setNewPassword] = useState('');
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const user = details.data?.user;
  useEffect(() => {
    if (user) setProfile({ displayName: user.displayName, email: user.email ?? '' });
  }, [user]);

  const run = async (work: () => Promise<unknown>, done: string = d.saved) => {
    setBusy(true);
    setMessage(null);
    try {
      await work();
      setMessage({ severity: 'success', text: done });
      await queryClient.invalidateQueries({ queryKey: ['admin-user', userId] });
      await queryClient.invalidateQueries({ queryKey: ['admin-users'] });
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    } finally {
      setBusy(false);
    }
  };

  const isMe = me?.id === userId;
  return (
    <Dialog
      open
      onClose={onClose}
      title={user?.displayName ?? '…'}
      maxWidth="md"
      footer={
        <Button variant="ghost" onClick={onClose}>
          {d.close}
        </Button>
      }
    >
      {details.isPending && <ProgressBar className="rounded-full" />}
      {details.isError && <Alert severity="error">{describeError(details.error)}</Alert>}
      {user && details.data && (
        <div className="space-y-5">
          <div className="flex flex-wrap gap-2">
            <Chip size="small" dir="ltr" label={user.username} />
            <Chip size="small" color={user.isActive ? 'success' : 'default'} label={user.isActive ? d.active : d.inactive} />
            {user.isSystemAdmin && <Chip size="small" color="secondary" label={d.systemAdmin} />}
            <Chip
              size="small"
              variant="outlined"
              label={`${d.lastLogin}: ${user.lastLoginAt ? formatDateTime(user.lastLoginAt) : d.neverLoggedIn}`}
            />
          </div>

          <form
            onSubmit={(event: FormEvent) => {
              event.preventDefault();
              void run(() => api.admin.updateUser(userId, { displayName: profile.displayName.trim(), email: profile.email.trim() || null }));
            }}
            className="space-y-4"
          >
            <div className="grid gap-4 sm:grid-cols-2">
              <TextField
                label={d.displayName}
                value={profile.displayName}
                onChange={(event) => setProfile({ ...profile, displayName: event.target.value })}
                required
              />
              <TextField label={d.email} value={profile.email} onChange={(event) => setProfile({ ...profile, email: event.target.value })} dir="ltr" />
            </div>
            <div className="flex flex-wrap gap-2">
              <Button type="submit" variant="outline" disabled={busy}>
                {d.save}
              </Button>
            </div>
          </form>

          <EntityPicker
            kind="User"
            label={d.manager}
            value={user.managerId}
            onChange={(managerId) => run(() => api.admin.setManager(userId, managerId))}
            disabled={busy}
          />

          <hr className="border-paper-200" />

          {!isMe && (
            <div className="flex flex-wrap gap-2">
              <Button variant={user.isActive ? 'danger' : 'outline'} disabled={busy} onClick={() => run(() => api.admin.setUserActive(userId, !user.isActive))}>
                {user.isActive ? d.deactivate : d.activate}
              </Button>
              {me?.isSystemAdmin && (
                <Button variant="outline" disabled={busy} onClick={() => run(() => api.admin.setUserAdmin(userId, !user.isSystemAdmin))}>
                  {user.isSystemAdmin ? d.revokeAdmin : d.makeAdmin}
                </Button>
              )}
            </div>
          )}

          {!isMe && (
            <form
              onSubmit={(event: FormEvent) => {
                event.preventDefault();
                void run(() => api.admin.resetPassword(userId, newPassword, true), d.passwordReset).then(() => setNewPassword(''));
              }}
              className="space-y-4"
            >
              <TextField
                label={d.resetPassword}
                type="password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                helperText={`${d.resetPasswordHelp} ${d.passwordHelp}`}
                autoComplete="new-password"
              />
              <div className="flex flex-wrap gap-2">
                <Button type="submit" variant="outline" disabled={busy || newPassword.length === 0}>
                  {d.resetPassword}
                </Button>
              </div>
            </form>
          )}

          <hr className="border-paper-200" />

          <div className="space-y-2">
            <h2 className="text-sm font-semibold text-ink-800">{d.memberOf}</h2>
            <div className="flex flex-wrap items-center gap-1.5">
              {details.data.groups.length === 0 && <span className="text-sm text-paper-500">{d.none}</span>}
              {details.data.groups.map((group) => (
                <Chip key={group.id} label={group.name} variant={group.isActive ? 'filled' : 'outlined'} />
              ))}
            </div>
          </div>

          {roles.isSuccess && (
            <div className="space-y-2">
              <h2 className="text-sm font-semibold text-ink-800">{d.holdsRoles}</h2>
              <div className="flex flex-wrap items-center gap-1.5">
                {roles.data.length === 0 && <span className="text-sm text-paper-500">{d.none}</span>}
                {roles.data.map((role) => (
                  <Chip key={role.id} label={role.name} />
                ))}
              </div>
            </div>
          )}

          {message && <Alert severity={message.severity}>{message.text}</Alert>}
        </div>
      )}
    </Dialog>
  );
}
