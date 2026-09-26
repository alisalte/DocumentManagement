import { useState, type FocusEvent } from 'react';
import { api, type DocumentVersion, type SharePermission } from '../../lib/api';
import { normalizeDigits } from '../../lib/dates';
import { describeError, t } from '../../strings';
import { EntityPicker } from '../metadata/EntityPicker';
import { Alert, Button, Checkbox, Dialog, Select, Tab, Tabs, TextArea, TextField } from '../ui';
import { permissionLabels, s } from './sharingStrings';

const shareDurations = [0, 1, 7, 30, 90];
const linkDurations = [1, 7, 30, 90];

function inDays(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString();
}

/**
 * Shares one published version, with a colleague or through an external link. Offers only what
 * the caller may share; the server checks it all again, including that nothing exceeds the
 * caller's own rights.
 */
export function ShareDialog({
  documentId,
  versions,
  canShare,
  canShareExternal,
  canDownload,
  canPrint,
  onClose,
  onShared,
}: {
  documentId: string;
  /** Published versions, newest first; the first is preselected. */
  versions: DocumentVersion[];
  canShare: boolean;
  canShareExternal: boolean;
  canDownload: boolean;
  canPrint: boolean;
  onClose: () => void;
  onShared: (message: string) => void;
}) {
  const [tab, setTab] = useState<'internal' | 'link'>(canShare ? 'internal' : 'link');
  const [versionId, setVersionId] = useState(versions[0]?.id ?? '');
  const [permissions, setPermissions] = useState<SharePermission[]>(['View']);
  const [recipientId, setRecipientId] = useState<string | null>(null);
  const [shareDays, setShareDays] = useState(0);
  const [message, setMessage] = useState('');
  const [linkDays, setLinkDays] = useState(7);
  const [maxOpenings, setMaxOpenings] = useState('');
  const [password, setPassword] = useState('');
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [createdUrl, setCreatedUrl] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const offered: SharePermission[] = ['View', ...(canDownload ? ['Download' as const] : []), ...(canPrint ? ['Print' as const] : [])];
  const toggle = (permission: SharePermission) =>
    setPermissions((current) =>
      current.includes(permission) ? current.filter((item) => item !== permission) : [...current, permission],
    );

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      if (tab === 'internal') {
        await api.sharing.share(documentId, {
          versionId,
          recipientId: recipientId!,
          permissions,
          expiresAt: shareDays > 0 ? inDays(shareDays) : null,
          message: message.trim() || null,
        });
        onShared(s.shared);
      } else {
        const openings = Number.parseInt(normalizeDigits(maxOpenings), 10);
        const created = await api.sharing.createLink(documentId, {
          versionId,
          permissions,
          expiresAt: inDays(linkDays),
          maxAccessCount: Number.isFinite(openings) && openings > 0 ? openings : null,
          password: password || null,
          label: label.trim() || null,
        });
        setCreatedUrl(`${window.location.origin}/s/${created.token}`);
      }
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const copy = async () => {
    if (!createdUrl) return;
    try {
      await navigator.clipboard.writeText(createdUrl);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  if (createdUrl) {
    return (
      <Dialog
        open
        onClose={() => onShared(s.linkCreated)}
        title={s.linkCreated}
        maxWidth="sm"
        footer={
          <>
            <Button variant="ghost" onClick={() => onShared(s.linkCreated)}>
              {s.close}
            </Button>
            <Button onClick={copy}>{s.copy}</Button>
          </>
        }
      >
        <div className="space-y-4">
          <Alert severity="warning">{s.linkShownOnce}</Alert>
          <TextField
            value={createdUrl}
            readOnly
            dir="ltr"
            style={{ fontFamily: 'monospace' }}
            onFocus={(event: FocusEvent<HTMLInputElement>) => event.target.select()}
          />
          {copied && <p className="text-sm text-emerald-600">{s.copied}</p>}
        </div>
      </Dialog>
    );
  }

  const ready = !!versionId && (tab === 'link' || !!recipientId);

  return (
    <Dialog
      open
      onClose={() => {
        if (!busy) onClose();
      }}
      title={s.share}
      maxWidth="sm"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            {t.cancel}
          </Button>
          <Button onClick={submit} loading={busy} disabled={busy || !ready}>
            {busy ? t.saving : tab === 'internal' ? s.shareAction : s.createLink}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {canShare && canShareExternal && (
          <Tabs value={tab} onChange={(value) => setTab(value as 'internal' | 'link')} variant="fullWidth">
            <Tab value="internal" label={s.withColleague} />
            <Tab value="link" label={s.externalLink} />
          </Tabs>
        )}

        {versions.length === 0 ? (
          <Alert severity="info">{s.onlyPublished}</Alert>
        ) : (
          <Select
            label={s.version}
            value={versionId}
            dir="ltr"
            onChange={(event) => setVersionId(event.target.value)}
            helperText={s.versionHelp}
          >
            {versions.map((version) => (
              <option key={version.id} value={version.id}>
                {`${version.label} — ${version.fileName}`}
              </option>
            ))}
          </Select>
        )}

        {tab === 'internal' && (
          <EntityPicker kind="User" label={s.recipient} value={recipientId} onChange={setRecipientId} required disabled={busy} />
        )}

        <div>
          <p className="mb-2 text-sm font-medium text-ink-800">{s.permissions}</p>
          <div className="flex flex-wrap gap-x-4 gap-y-2">
            {offered.map((permission) => (
              <Checkbox
                key={permission}
                label={permissionLabels[permission]}
                checked={permissions.includes(permission)}
                onChange={() => toggle(permission)}
                // Nothing works without viewing, so it is always part of a share.
                disabled={permission === 'View' || busy}
              />
            ))}
          </div>
        </div>

        {tab === 'internal' ? (
          <>
            <Select label={s.expiry} value={shareDays} onChange={(event) => setShareDays(Number(event.target.value))}>
              {shareDurations.map((days) => (
                <option key={days} value={days}>
                  {days === 0 ? s.noExpiry : s.days(days)}
                </option>
              ))}
            </Select>
            <TextArea label={s.message} value={message} onChange={(event) => setMessage(event.target.value)} rows={2} />
          </>
        ) : (
          <>
            <Select label={s.expiry} value={linkDays} onChange={(event) => setLinkDays(Number(event.target.value))}>
              {linkDurations.map((days) => (
                <option key={days} value={days}>
                  {s.days(days)}
                </option>
              ))}
            </Select>
            <TextField
              label={s.maxOpenings}
              value={maxOpenings}
              onChange={(event) => setMaxOpenings(event.target.value)}
              helperText={s.maxOpeningsHelp}
              inputMode="numeric"
            />
            <TextField
              label={s.linkPassword}
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              helperText={s.linkPasswordHelp}
              autoComplete="new-password"
            />
            <TextField label={s.linkLabel} value={label} onChange={(event) => setLabel(event.target.value)} />
          </>
        )}

        {error && <Alert severity="error">{error}</Alert>}
      </div>
    </Dialog>
  );
}
