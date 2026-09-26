import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api, type AclEntry, type AclResourceType, type EffectivePermission, type SubjectType } from '../../lib/api';
import { d, permissionLabel, reasonLabels } from '../../pages/admin/directoryStrings';
import { describeError } from '../../strings';
import { EntityPicker } from '../metadata/EntityPicker';
import { Alert, Button, Checkbox, Chip, Dialog, IconButton, ProgressBar, Select, Tab, Tabs, TextField, Tooltip, cx } from '../ui';

interface Props {
  resourceType: AclResourceType;
  resourceId: string;
  resourceName: string;
  open: boolean;
  onClose: () => void;
}

/**
 * The access control list of a category or a document, with what reaches it from the categories
 * above, and a "why?" view that shows for one person which entry decided each permission. Deny
 * wins over every allow (section 5.4), which is only usable when the reason is visible.
 */
export function AclEditor({ resourceType, resourceId, resourceName, open, onClose }: Props) {
  const [tab, setTab] = useState<'acl' | 'why'>('acl');

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={`${d.permissionsTitle}: ${resourceName}`}
      maxWidth="md"
      footer={
        <Button variant="ghost" onClick={onClose}>
          {d.close}
        </Button>
      }
    >
      <div className="-mx-5 -mt-4 px-5">
        <Tabs value={tab} onChange={(value) => setTab(value as 'acl' | 'why')}>
          <Tab value="acl" label={d.acl} />
          <Tab value="why" label={d.why} />
        </Tabs>
      </div>
      <div className="mt-4 space-y-4">
        {open && tab === 'acl' && <EntriesTab resourceType={resourceType} resourceId={resourceId} />}
        {open && tab === 'why' && <WhyTab resourceType={resourceType} resourceId={resourceId} />}
      </div>
    </Dialog>
  );
}

function useCategoryNames() {
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.categories });
  return (id: string) => categories.data?.find((category) => category.id === id)?.name ?? id;
}

function useGrantablePermissions() {
  return useQuery({
    queryKey: ['permission-catalog'],
    queryFn: api.acl.catalog,
    staleTime: Infinity,
    select: (catalog) => catalog.filter((definition) => definition.scope !== 'System'),
  });
}

function EntriesTab({ resourceType, resourceId }: { resourceType: AclResourceType; resourceId: string }) {
  const queryClient = useQueryClient();
  const key = ['acl', resourceType, resourceId];
  const entries = useQuery({ queryKey: key, queryFn: () => api.acl.list(resourceType, resourceId) });
  const categoryName = useCategoryNames();
  const [error, setError] = useState<string | null>(null);

  const refreshAccess = async () => {
    await queryClient.invalidateQueries({ queryKey: key });
    // canCreate / canView on the folder tree come from this query — without it the "ثبت سند"
    // screen stays empty after a grant until a full reload.
    await queryClient.invalidateQueries({ queryKey: ['categories'] });
  };

  const revoke = async (entry: AclEntry) => {
    setError(null);
    try {
      await api.acl.revoke(entry.id);
      await refreshAccess();
    } catch (caught) {
      setError(describeError(caught));
    }
  };

  const rows = entries.data ?? [];
  return (
    <div className="space-y-4">
      {entries.isPending && <ProgressBar />}
      {entries.isError && <Alert severity="error">{describeError(entries.error)}</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
      {entries.isSuccess && rows.length === 0 && <p className="py-8 text-center text-sm text-paper-500">{d.empty}</p>}

      <div className="space-y-2">
        {rows.map((entry) => (
          <div
            key={entry.id}
            className={cx('rounded-lg border border-paper-200 bg-white p-3', entry.isInherited && 'opacity-[0.85]')}
          >
            <div className="flex flex-wrap items-center gap-2">
              <Chip
                size="small"
                color={entry.effect === 'Deny' ? 'error' : 'success'}
                label={entry.effect === 'Deny' ? d.deny : d.allow}
              />
              <span className="text-sm font-semibold text-ink-800">{permissionLabel(entry.permissionCode)}</span>
              <span className="text-sm text-paper-500">
                {d.subjectTypes[entry.subjectType]}: {entry.subjectName ?? entry.subjectId}
              </span>
              <div className="flex-1" />
              {entry.isInherited ? (
                <Chip size="small" variant="outlined" label={`${d.inheritedFrom} «${categoryName(entry.resourceId)}»`} />
              ) : (
                <>
                  {entry.inherit && resourceType === 'Category' && <Chip size="small" variant="outlined" label={d.inherit} />}
                  <Tooltip label={d.revoke}>
                    <IconButton
                      label={d.revoke}
                      size="sm"
                      className="text-rose-500 hover:bg-rose-50 hover:text-rose-700"
                      onClick={() => revoke(entry)}
                    >
                      ✕
                    </IconButton>
                  </Tooltip>
                </>
              )}
            </div>
            {entry.reason && <p className="mt-1.5 text-xs text-paper-500">{entry.reason}</p>}
          </div>
        ))}
      </div>

      <GrantForm resourceType={resourceType} resourceId={resourceId} onGranted={refreshAccess} />
    </div>
  );
}

/** Minimum ACL pair the filing UI requires (see ListCategoriesHandler.CanCreate). */
const filingPair = ['DOCUMENT_VIEW', 'DOCUMENT_CREATE'] as const;

function GrantForm({ resourceType, resourceId, onGranted }: { resourceType: AclResourceType; resourceId: string; onGranted: () => void | Promise<void> }) {
  const permissions = useGrantablePermissions();
  const roles = useQuery({ queryKey: ['admin-roles'], queryFn: () => api.admin.roles(), retry: false });
  const [subjectType, setSubjectType] = useState<SubjectType>('User');
  const [subjectId, setSubjectId] = useState<string | null>(null);
  const [permissionCode, setPermissionCode] = useState('DOCUMENT_CREATE');
  const [effect, setEffect] = useState<'Allow' | 'Deny'>('Allow');
  const [inherit, setInherit] = useState(true);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const grantOne = async (code: string) => {
    await api.acl.grant(resourceType, resourceId, {
      subjectType,
      subjectId: subjectId!,
      permissionCode: code,
      effect,
      inherit: resourceType === 'Category' ? inherit : false,
      reason: reason.trim() || null,
    });
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!subjectId) return;
    setBusy(true);
    setError(null);
    try {
      await grantOne(permissionCode);
      setSubjectId(null);
      setReason('');
      await onGranted();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const grantFiling = async () => {
    if (!subjectId) return;
    setBusy(true);
    setError(null);
    try {
      const existing = await api.acl.list(resourceType, resourceId, false);
      const mine = new Set(
        existing
          .filter((entry) => entry.subjectType === subjectType && entry.subjectId === subjectId && entry.effect === 'Allow')
          .map((entry) => entry.permissionCode),
      );
      for (const code of filingPair) {
        if (!mine.has(code)) await grantOne(code);
      }
      setSubjectId(null);
      setReason('');
      await onGranted();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="rounded-xl border border-paper-200 bg-paper-50/70 p-4">
      <div className="space-y-4">
        <h3 className="text-sm font-semibold text-ink-800">{d.grant}</h3>
        {resourceType === 'Category' && <p className="text-xs text-paper-500">{d.filingNeedsViewAndCreate}</p>}
        <div className="grid gap-4 sm:grid-cols-[10rem_1fr]">
          <Select
            label={d.subjectType}
            value={subjectType}
            onChange={(event) => {
              setSubjectType(event.target.value as SubjectType);
              setSubjectId(null);
            }}
          >
            <option value="User">{d.subjectTypes.User}</option>
            <option value="Group">{d.subjectTypes.Group}</option>
            <option value="Role" disabled={!roles.isSuccess}>
              {d.subjectTypes.Role}
            </option>
          </Select>
          {subjectType === 'Role' ? (
            <Select
              label={d.subject}
              value={subjectId ?? ''}
              onChange={(event) => setSubjectId(event.target.value || null)}
              required
            >
              {(roles.data ?? []).map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name}
                </option>
              ))}
            </Select>
          ) : (
            <EntityPicker kind={subjectType} label={d.subject} value={subjectId} onChange={setSubjectId} required />
          )}
          <Select label={d.permission} value={permissionCode} onChange={(event) => setPermissionCode(event.target.value)}>
            {(permissions.data ?? []).map((definition) => (
              <option key={definition.code} value={definition.code}>
                {permissionLabel(definition.code)}
              </option>
            ))}
          </Select>
          <Select
            label={d.effect}
            value={effect}
            onChange={(event) => setEffect(event.target.value as 'Allow' | 'Deny')}
            helperText={effect === 'Deny' ? d.denyHelp : undefined}
          >
            <option value="Allow">{d.allow}</option>
            <option value="Deny">{d.deny}</option>
          </Select>
        </div>
        {resourceType === 'Category' && (
          <Checkbox label={d.inherit} checked={inherit} onChange={(event) => setInherit(event.target.checked)} />
        )}
        <TextField label={d.reason} value={reason} onChange={(event) => setReason(event.target.value)} />
        {error && <Alert severity="error">{error}</Alert>}
        <div className="flex flex-wrap justify-end gap-2">
          {resourceType === 'Category' && effect === 'Allow' && (
            <Button type="button" variant="outline" loading={busy} disabled={busy || !subjectId} onClick={grantFiling}>
              {d.grantFilingAccess}
            </Button>
          )}
          <Button type="submit" loading={busy} disabled={busy || !subjectId}>
            {d.grant}
          </Button>
        </div>
      </div>
    </form>
  );
}

function WhyTab({ resourceType, resourceId }: { resourceType: AclResourceType; resourceId: string }) {
  const [userId, setUserId] = useState<string | null>(null);
  const categoryName = useCategoryNames();
  const explained = useQuery({
    queryKey: ['acl-why', resourceType, resourceId, userId],
    queryFn: () => api.acl.explain(userId!, resourceType, resourceId),
    enabled: !!userId,
  });

  const where = (item: EffectivePermission) =>
    !item.source
      ? ''
      : item.source.resourceType === resourceType && item.source.resourceId === resourceId
        ? d.thisResource
        : `«${categoryName(item.source.resourceId)}»`;

  return (
    <div className="space-y-4">
      <p className="text-sm text-paper-500">{d.whyHelp}</p>
      <EntityPicker kind="User" label={d.users} value={userId} onChange={setUserId} />
      {explained.isFetching && <ProgressBar />}
      {explained.isError && <Alert severity="error">{describeError(explained.error)}</Alert>}
      <div className="space-y-2">
        {(explained.data ?? []).map((item) => (
          <div key={item.permissionCode} className="rounded-lg border border-paper-200 bg-white p-3">
            <div className="flex flex-wrap items-center gap-2">
              <Chip size="small" color={item.allowed ? 'success' : 'default'} label={item.allowed ? d.allowed : d.denied} />
              <span className="text-sm font-semibold text-ink-800">{permissionLabel(item.permissionCode)}</span>
              <span className={cx('text-sm', item.reason === 'DeniedByExplicitDeny' ? 'text-rose-600' : 'text-paper-500')}>
                {reasonLabels[item.reason] ?? item.reason}
              </span>
            </div>
            {item.source && (
              <p className="mt-1.5 text-xs text-paper-500">
                {d.decidedBy} {item.source.effect === 'Deny' ? d.deny : d.allow} {where(item)} {d.subjectTypes[item.source.subjectType]}{' '}
                «{item.source.subjectName ?? item.source.subjectId}»
              </p>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
