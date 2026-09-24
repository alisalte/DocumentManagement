import {
  Alert,
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  FormGroup,
  FormLabel,
  MenuItem,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import { useState, type FocusEvent } from 'react';
import { api, type DocumentVersion, type SharePermission } from '../../lib/api';
import { normalizeDigits } from '../../lib/dates';
import { describeError, t } from '../../strings';
import { EntityPicker } from '../metadata/EntityPicker';
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
      <Dialog open onClose={() => onShared(s.linkCreated)} fullWidth maxWidth="sm">
        <DialogTitle>{s.linkCreated}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Alert severity="warning">{s.linkShownOnce}</Alert>
            <TextField
              value={createdUrl}
              fullWidth
              slotProps={{ htmlInput: { readOnly: true, dir: 'ltr', onFocus: (event: FocusEvent<HTMLInputElement>) => event.target.select() } }}
            />
            {copied && <Typography color="success.main">{s.copied}</Typography>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={copy} variant="contained">
            {s.copy}
          </Button>
          <Button onClick={() => onShared(s.linkCreated)}>{s.close}</Button>
        </DialogActions>
      </Dialog>
    );
  }

  const ready = !!versionId && (tab === 'link' || !!recipientId);

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{s.share}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {canShare && canShareExternal && (
            <Tabs value={tab} onChange={(_, next) => setTab(next)} variant="fullWidth">
              <Tab value="internal" label={s.withColleague} />
              <Tab value="link" label={s.externalLink} />
            </Tabs>
          )}

          {versions.length === 0 ? (
            <Alert severity="info">{s.onlyPublished}</Alert>
          ) : (
            <TextField
              select
              label={s.version}
              value={versionId}
              onChange={(event) => setVersionId(event.target.value)}
              helperText={s.versionHelp}
              fullWidth
            >
              {versions.map((version) => (
                <MenuItem key={version.id} value={version.id}>
                  <Box component="span" dir="ltr">{version.label}</Box>
                  <Box component="span" sx={{ mx: 1, color: 'text.secondary' }}>{version.fileName}</Box>
                </MenuItem>
              ))}
            </TextField>
          )}

          {tab === 'internal' && (
            <EntityPicker kind="User" label={s.recipient} value={recipientId} onChange={setRecipientId} required disabled={busy} />
          )}

          <Box>
            <FormLabel component="legend">{s.permissions}</FormLabel>
            <FormGroup row>
              {offered.map((permission) => (
                <FormControlLabel
                  key={permission}
                  control={
                    <Checkbox
                      checked={permissions.includes(permission)}
                      onChange={() => toggle(permission)}
                      // Nothing works without viewing, so it is always part of a share.
                      disabled={permission === 'View' || busy}
                    />
                  }
                  label={permissionLabels[permission]}
                />
              ))}
            </FormGroup>
          </Box>

          {tab === 'internal' ? (
            <>
              <TextField select label={s.expiry} value={shareDays} onChange={(event) => setShareDays(Number(event.target.value))} fullWidth>
                {shareDurations.map((days) => (
                  <MenuItem key={days} value={days}>
                    {days === 0 ? s.noExpiry : s.days(days)}
                  </MenuItem>
                ))}
              </TextField>
              <TextField label={s.message} value={message} onChange={(event) => setMessage(event.target.value)} multiline minRows={2} fullWidth />
            </>
          ) : (
            <>
              <TextField select label={s.expiry} value={linkDays} onChange={(event) => setLinkDays(Number(event.target.value))} fullWidth>
                {linkDurations.map((days) => (
                  <MenuItem key={days} value={days}>
                    {s.days(days)}
                  </MenuItem>
                ))}
              </TextField>
              <TextField
                label={s.maxOpenings}
                value={maxOpenings}
                onChange={(event) => setMaxOpenings(event.target.value)}
                helperText={s.maxOpeningsHelp}
                slotProps={{ htmlInput: { inputMode: 'numeric' } }}
                fullWidth
              />
              <TextField
                label={s.linkPassword}
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                helperText={s.linkPasswordHelp}
                autoComplete="new-password"
                fullWidth
              />
              <TextField label={s.linkLabel} value={label} onChange={(event) => setLabel(event.target.value)} fullWidth />
            </>
          )}

          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          {t.cancel}
        </Button>
        <Button variant="contained" onClick={submit} disabled={busy || !ready}>
          {busy ? t.saving : tab === 'internal' ? s.shareAction : s.createLink}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
