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
  TextField,
} from '../../components/ui';
import { api, type AdminGroup } from '../../lib/api';
import { describeError } from '../../strings';
import { d } from './directoryStrings';

/** Groups and their members (ADMIN_MANAGE_GROUPS). Groups are ACL subjects and workflow assignees. */
export function GroupsPage() {
  const groups = useQuery({ queryKey: ['admin-groups'], queryFn: api.admin.groups });
  const [creating, setCreating] = useState(false);
  const [selected, setSelected] = useState<AdminGroup | null>(null);

  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-bold text-slate-800">{d.groups}</h1>
        <Button onClick={() => setCreating(true)}>{d.newGroup}</Button>
      </div>

      <Card flush>
        {groups.isPending && <ProgressBar className="rounded-t-xl" />}
        {groups.isError && (
          <div className="p-4">
            <Alert severity="error">{describeError(groups.error)}</Alert>
          </div>
        )}
        {groups.isSuccess && groups.data.length === 0 && (
          <p className="py-10 text-center text-sm text-slate-500">{d.empty}</p>
        )}
        {groups.data && groups.data.length > 0 && (
          <Table dense>
            <THead>
              <TR>
                <TH>{d.name}</TH>
                <TH>{d.code}</TH>
                <TH />
              </TR>
            </THead>
            <TBody>
              {groups.data.map((group) => (
                <TR
                  key={group.id}
                  role="button"
                  tabIndex={0}
                  className="cursor-pointer"
                  onClick={() => setSelected(group)}
                  onKeyDown={(event) => event.key === 'Enter' && setSelected(group)}
                >
                  <TD>
                    <span className="font-semibold text-slate-800">{group.name}</span>
                  </TD>
                  <TD>
                    <span dir="ltr" className="text-slate-500">
                      {group.code}
                    </span>
                  </TD>
                  <TD className="text-end whitespace-nowrap">{!group.isActive && <Chip size="small" label={d.inactive} />}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </Card>

      <CreateGroupDialog open={creating} onClose={() => setCreating(false)} />
      {selected && <GroupDialog group={selected} onClose={() => setSelected(null)} />}
    </div>
  );
}

function CreateGroupDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      await api.admin.createGroup({ code: code.trim(), name: name.trim() });
      await queryClient.invalidateQueries({ queryKey: ['admin-groups'] });
      setCode('');
      setName('');
      onClose();
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={d.newGroup}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {d.cancel}
          </Button>
          <Button type="submit" form="admin-create-group">
            {d.create}
          </Button>
        </>
      }
    >
      <form id="admin-create-group" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <TextField
          label={d.code}
          value={code}
          onChange={(event) => setCode(event.target.value)}
          helperText={d.codeHelp}
          required
          dir="ltr"
          className="sm:col-span-2"
        />
        <TextField label={d.name} value={name} onChange={(event) => setName(event.target.value)} required className="sm:col-span-2" />
        {error && (
          <Alert severity="error" className="sm:col-span-2">
            {error}
          </Alert>
        )}
      </form>
    </Dialog>
  );
}

function GroupDialog({ group, onClose }: { group: AdminGroup; onClose: () => void }) {
  const queryClient = useQueryClient();
  const members = useQuery({ queryKey: ['admin-group-members', group.id], queryFn: () => api.admin.groupMembers(group.id) });
  const [name, setName] = useState(group.name);
  const [isActive, setIsActive] = useState(group.isActive);
  const [adding, setAdding] = useState<string | null>(null);
  const [message, setMessage] = useState<{ severity: 'success' | 'error'; text: string } | null>(null);

  useEffect(() => {
    setName(group.name);
    setIsActive(group.isActive);
  }, [group]);

  const run = async (work: () => Promise<unknown>, done?: string) => {
    setMessage(null);
    try {
      await work();
      if (done) setMessage({ severity: 'success', text: done });
      await queryClient.invalidateQueries({ queryKey: ['admin-group-members', group.id] });
      await queryClient.invalidateQueries({ queryKey: ['admin-groups'] });
    } catch (caught) {
      setMessage({ severity: 'error', text: describeError(caught) });
    }
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={
        <>
          {group.name}{' '}
          <span dir="ltr" className="text-slate-500">
            {group.code}
          </span>
        </>
      }
      maxWidth="md"
      footer={
        <Button variant="ghost" onClick={onClose}>
          {d.close}
        </Button>
      }
    >
      <div className="space-y-4 sm:space-y-5">
        <form
          onSubmit={(event: FormEvent) => {
            event.preventDefault();
            void run(() => api.admin.updateGroup(group.id, { name: name.trim(), isActive }), d.saved);
          }}
          className="space-y-4"
        >
          <TextField label={d.name} value={name} onChange={(event) => setName(event.target.value)} required />
          <Checkbox label={d.active} checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
          {!isActive && <Alert severity="info">{d.groupInactiveHelp}</Alert>}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="outline">
              {d.save}
            </Button>
          </div>
        </form>

        <div className="space-y-2">
          <h2 className="text-sm font-semibold text-slate-700">{d.members}</h2>
          {members.isPending && <ProgressBar className="rounded-full" />}
          {members.isError && <Alert severity="error">{describeError(members.error)}</Alert>}

          <div className="space-y-1">
            {members.isSuccess && members.data.length === 0 && (
              <p className="py-6 text-center text-sm text-slate-500">{d.empty}</p>
            )}
            {(members.data ?? []).map((member) => (
              <div key={member.id} className="flex items-center gap-2 rounded-lg px-2 py-1.5 transition-colors hover:bg-slate-50">
                <span className={`min-w-0 flex-1 text-sm ${member.isActive ? '' : 'opacity-60'}`}>
                  {member.displayName}{' '}
                  <span dir="ltr" className="text-xs text-slate-500">
                    {member.username}
                  </span>
                </span>
                <IconButton label={d.remove} size="sm" onClick={() => run(() => api.admin.removeMember(group.id, member.id))}>
                  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5">
                    <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
                  </svg>
                </IconButton>
              </div>
            ))}
          </div>

          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <div className="min-w-0 flex-1">
              <EntityPicker kind="User" label={d.addMember} value={adding} onChange={setAdding} />
            </div>
            <Button
              disabled={!adding}
              onClick={() => adding && run(() => api.admin.addMember(group.id, adding)).then(() => setAdding(null))}
            >
              {d.add}
            </Button>
          </div>
        </div>

        {message && <Alert severity={message.severity}>{message.text}</Alert>}
      </div>
    </Dialog>
  );
}
