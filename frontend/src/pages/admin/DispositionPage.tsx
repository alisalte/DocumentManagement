import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Chip,
  Dialog,
  ProgressBar,
  Table,
  TBody,
  TD,
  TH,
  THead,
  TR,
  TextArea,
  TextField,
  Toast,
} from '../../components/ui';
import { api, type DestructionCertificate, type PendingDisposalItem } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { describeError } from '../../strings';
import { useSession } from '../../session';
import { actionLabel } from './auditStrings';
import { d, recordStatusLabel } from './dispositionStrings';

type DialogState =
  | { kind: 'request'; item: PendingDisposalItem }
  | { kind: 'approve'; item: PendingDisposalItem; dispositionId: string }
  | { kind: 'reject'; item: PendingDisposalItem; dispositionId: string }
  | { kind: 'destroy'; item: PendingDisposalItem; dispositionId: string }
  | { kind: 'certificate'; certificate: DestructionCertificate }
  | null;

function statusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'PendingReview':
      return d.pendingReview;
    case 'Approved':
      return d.approved;
    case 'Rejected':
      return d.rejected;
    case 'Destroyed':
      return d.destroyed;
    default:
      return status || d.noDisposition;
  }
}

/** Admin surface for PendingDisposal → disposition review → destroy → Certificate of Destruction. */
export function DispositionPage() {
  const { user } = useSession();
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<DialogState>(null);
  const [reason, setReason] = useState('');
  const [confirmWord, setConfirmWord] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [auditFor, setAuditFor] = useState<string | null>(null);

  const canRequest = !!user && (user.isSystemAdmin || user.systemPermissions.includes('DISPOSITION_REQUEST'));
  const canApprove = !!user && (user.isSystemAdmin || user.systemPermissions.includes('DISPOSITION_APPROVE'));
  const canDestroy = !!user && (user.isSystemAdmin || user.systemPermissions.includes('DISPOSITION_DESTROY'));
  const canViewCert =
    !!user &&
    (user.isSystemAdmin ||
      user.systemPermissions.includes('DISPOSITION_VIEW_CERTIFICATE') ||
      user.systemPermissions.includes('DISPOSITION_APPROVE') ||
      user.systemPermissions.includes('DISPOSITION_DESTROY'));

  const pending = useQuery({
    queryKey: ['disposition-pending'],
    queryFn: api.disposition.pending,
  });

  const audit = useQuery({
    queryKey: ['disposition-audit', auditFor],
    queryFn: () => api.audit.list({ entityId: auditFor }, 0, 50),
    enabled: !!auditFor && !!user && (user.isSystemAdmin || user.systemPermissions.includes('AUDIT_VIEW')),
  });

  const rows = useMemo(() => pending.data ?? [], [pending.data]);

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['disposition-pending'] });
  };

  const open = (next: DialogState) => {
    setError(null);
    setReason('');
    setConfirmWord('');
    setDialog(next);
  };

  const run = async (action: () => Promise<void>, success: string) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      setNotice(success);
      setDialog(null);
      await refresh();
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const showCertificate = async (recordId: string) => {
    setBusy(true);
    setError(null);
    try {
      const certificate = await api.disposition.certificateByRecord(recordId);
      open({ kind: 'certificate', certificate });
      setAuditFor(certificate.dispositionId);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const exportCertificate = (certificate: DestructionCertificate) => {
    const blob = new Blob([JSON.stringify(certificate, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${certificate.certificateNumber}.json`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">{d.title}</h1>
        <p className="mt-1 text-sm text-paper-600">{d.subtitle}</p>
      </div>

      {error && <Alert severity="error">{error}</Alert>}

      <Card flush className="overflow-hidden">
        {pending.isFetching && <ProgressBar />}
        {pending.isError ? (
          <div className="p-4">
            <Alert severity="error">{describeError(pending.error)}</Alert>
          </div>
        ) : rows.length === 0 ? (
          <p className="p-6 text-sm text-paper-600">{d.empty}</p>
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TR>
                  <TH>{d.recordTitle}</TH>
                  <TH>{d.status}</TH>
                  <TH>{d.retentionExpiry}</TH>
                  <TH>{d.legalHold}</TH>
                  <TH>{d.disposition}</TH>
                  <TH>{d.finalVersion}</TH>
                  <TH />
                </TR>
              </THead>
              <TBody>
                {rows.map((item) => (
                  <TR key={item.recordId}>
                    <TD>
                      <div className="font-medium text-ink-900">{item.title}</div>
                      <div className="text-xs text-paper-500 font-mono ltr">{item.recordId}</div>
                    </TD>
                    <TD>
                      <Chip label={recordStatusLabel(item.status)} color="primary" />
                    </TD>
                    <TD>{item.retentionExpiresAt ? formatDateTime(item.retentionExpiresAt) : d.none}</TD>
                    <TD>
                      {item.onLegalHold ? (
                        <Chip label={d.onHold} color="error" />
                      ) : (
                        <span className="text-paper-600">{d.notOnHold}</span>
                      )}
                    </TD>
                    <TD>{statusLabel(item.activeDispositionStatus)}</TD>
                    <TD className="font-mono ltr">{item.finalVersionLabel}</TD>
                    <TD>
                      <div className="flex flex-wrap justify-end gap-2">
                        {canRequest &&
                          item.status === 'PendingDisposal' &&
                          !item.activeDispositionId &&
                          !item.onLegalHold && (
                            <Button size="sm" variant="ghost" onClick={() => open({ kind: 'request', item })}>
                              {d.request}
                            </Button>
                          )}
                        {canApprove &&
                          item.activeDispositionStatus === 'PendingReview' &&
                          item.activeDispositionId && (
                            <>
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() =>
                                  open({
                                    kind: 'approve',
                                    item,
                                    dispositionId: item.activeDispositionId!,
                                  })
                                }
                              >
                                {d.approve}
                              </Button>
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() =>
                                  open({
                                    kind: 'reject',
                                    item,
                                    dispositionId: item.activeDispositionId!,
                                  })
                                }
                              >
                                {d.reject}
                              </Button>
                            </>
                          )}
                        {canDestroy &&
                          item.activeDispositionStatus === 'Approved' &&
                          item.activeDispositionId && (
                            <Button
                              size="sm"
                              variant="danger"
                              onClick={() =>
                                open({
                                  kind: 'destroy',
                                  item,
                                  dispositionId: item.activeDispositionId!,
                                })
                              }
                            >
                              {d.destroy}
                            </Button>
                          )}
                        {canViewCert && (item.status === 'Destroyed' || item.activeDispositionStatus === 'Destroyed') && (
                          <Button size="sm" variant="ghost" onClick={() => void showCertificate(item.recordId)}>
                            {d.viewCertificate}
                          </Button>
                        )}
                        {item.activeDispositionId && (
                          <Button size="sm" variant="ghost" onClick={() => setAuditFor(item.activeDispositionId)}>
                            {d.auditHistory}
                          </Button>
                        )}
                      </div>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
        )}
      </Card>

      {auditFor && (
        <Card className="space-y-3 p-4">
          <div className="flex items-center justify-between gap-3">
            <h2 className="text-base font-semibold text-ink-900">{d.auditHistory}</h2>
            <Button size="sm" variant="ghost" onClick={() => setAuditFor(null)}>
              {d.close}
            </Button>
          </div>
          {audit.isFetching && <ProgressBar />}
          {audit.isError && <Alert severity="error">{describeError(audit.error)}</Alert>}
          {audit.data && audit.data.length === 0 && <p className="text-sm text-paper-600">{d.none}</p>}
          {audit.data && audit.data.length > 0 && (
            <ul className="space-y-2 text-sm">
              {audit.data.map((entry) => (
                <li key={entry.id} className="flex flex-wrap gap-2 border-b border-paper-100 pb-2">
                  <span className="font-mono ltr text-xs text-paper-500">{formatDateTime(entry.occurredAt)}</span>
                  <Chip label={actionLabel(entry.action)} color="secondary" />
                  <span className="text-paper-600">{entry.outcome}</span>
                  <span className="text-paper-500">{entry.userName ?? entry.actorType}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      )}

      <Dialog
        open={dialog?.kind === 'request' || dialog?.kind === 'approve' || dialog?.kind === 'reject'}
        onClose={() => !busy && setDialog(null)}
        title={
          dialog?.kind === 'request' ? d.request : dialog?.kind === 'approve' ? d.approve : d.reject
        }
        footer={
          <>
            <Button variant="ghost" disabled={busy} onClick={() => setDialog(null)}>
              {d.cancel}
            </Button>
            <Button
              disabled={busy || (dialog?.kind === 'reject' && !reason.trim())}
              onClick={() => {
                if (!dialog || dialog.kind === 'certificate' || dialog.kind === 'destroy') return;
                if (dialog.kind === 'request') {
                  void run(
                    () => api.disposition.request(dialog.item.recordId, reason || null).then(() => undefined),
                    d.requested,
                  );
                } else if (dialog.kind === 'approve') {
                  void run(
                    () => api.disposition.approve(dialog.dispositionId, reason || null),
                    d.approvedOk,
                  );
                } else {
                  void run(() => api.disposition.reject(dialog.dispositionId, reason), d.rejectedOk);
                }
              }}
            >
              {d.submit}
            </Button>
          </>
        }
      >
        {dialog && dialog.kind !== 'certificate' && dialog.kind !== 'destroy' && (
          <div className="space-y-3">
            <p className="text-sm text-ink-800">{dialog.item.title}</p>
            <TextArea
              label={dialog.kind === 'reject' ? d.rejectionReason : d.reason}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              required={dialog.kind === 'reject'}
            />
            {dialog.kind === 'reject' && !reason.trim() && (
              <p className="text-xs text-danger-700">{d.rejectionRequired}</p>
            )}
          </div>
        )}
      </Dialog>

      <Dialog
        open={dialog?.kind === 'destroy'}
        onClose={() => !busy && setDialog(null)}
        title={d.confirmDestroyTitle}
        maxWidth="lg"
        footer={
          <>
            <Button variant="ghost" disabled={busy} onClick={() => setDialog(null)}>
              {d.cancel}
            </Button>
            <Button
              variant="danger"
              disabled={busy || confirmWord.trim() !== d.confirmDestroyWord}
              onClick={() => {
                if (dialog?.kind !== 'destroy') return;
                const dispositionId = dialog.dispositionId;
                void (async () => {
                  setBusy(true);
                  setError(null);
                  try {
                    const result = await api.disposition.destroy(dispositionId, reason || null);
                    setNotice(d.destroyedOk);
                    await refresh();
                    if (canViewCert) {
                      const certificate = await api.disposition.certificate(result.certificateId);
                      setDialog({ kind: 'certificate', certificate });
                      setAuditFor(certificate.dispositionId);
                    } else {
                      setDialog(null);
                    }
                  } catch (caught) {
                    setError(describeError(caught));
                  } finally {
                    setBusy(false);
                  }
                })();
              }}
            >
              {d.destroy}
            </Button>
          </>
        }
      >
        {dialog?.kind === 'destroy' && (
          <div className="space-y-3 text-sm">
            <Alert severity="warning">{d.confirmDestroyBody}</Alert>
            <dl className="grid gap-2 sm:grid-cols-2">
              <div>
                <dt className="text-paper-500">{d.recordTitle}</dt>
                <dd className="font-medium">{dialog.item.title}</dd>
              </div>
              <div>
                <dt className="text-paper-500">{d.finalVersion}</dt>
                <dd className="font-mono ltr">{dialog.item.finalVersionLabel}</dd>
              </div>
              <div>
                <dt className="text-paper-500">{d.retentionExpiry}</dt>
                <dd>{dialog.item.retentionExpiresAt ? formatDateTime(dialog.item.retentionExpiresAt) : d.none}</dd>
              </div>
              <div>
                <dt className="text-paper-500">{d.legalHold}</dt>
                <dd>{dialog.item.onLegalHold ? d.onHold : d.notOnHold}</dd>
              </div>
              <div>
                <dt className="text-paper-500">{d.disposition}</dt>
                <dd>{statusLabel(dialog.item.activeDispositionStatus)}</dd>
              </div>
            </dl>
            <TextArea label={d.reason} value={reason} onChange={(event) => setReason(event.target.value)} />
            <TextField
              label={d.confirmDestroyLabel}
              value={confirmWord}
              onChange={(event) => setConfirmWord(event.target.value)}
            />
          </div>
        )}
      </Dialog>

      <Dialog
        open={dialog?.kind === 'certificate'}
        onClose={() => setDialog(null)}
        title={d.certificateTitle}
        maxWidth="lg"
        footer={
          dialog?.kind === 'certificate' ? (
            <>
              <Button variant="ghost" onClick={() => exportCertificate(dialog.certificate)}>
                {d.exportJson}
              </Button>
              <Button onClick={() => setDialog(null)}>{d.close}</Button>
            </>
          ) : undefined
        }
      >
        {dialog?.kind === 'certificate' && (
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <CertField label={d.certificateNumber} value={dialog.certificate.certificateNumber} mono />
            <CertField label={d.recordTitle} value={dialog.certificate.recordTitle} />
            <CertField label={d.finalVersion} value={dialog.certificate.finalVersionLabel} mono />
            <CertField label={d.contentHash} value={dialog.certificate.contentSha256} mono />
            <CertField label={d.certificateHash} value={dialog.certificate.certificateHash} mono />
            <CertField
              label={d.retentionPolicy}
              value={
                dialog.certificate.retentionPolicyId
                  ? `${dialog.certificate.retentionPolicyId} v${dialog.certificate.retentionPolicyVersion ?? '—'}`
                  : d.none
              }
              mono
            />
            <CertField
              label={d.retentionExpiry}
              value={
                dialog.certificate.retentionExpiresAt
                  ? formatDateTime(dialog.certificate.retentionExpiresAt)
                  : d.none
              }
            />
            <CertField
              label={d.legalHoldCheckedAt}
              value={formatDateTime(dialog.certificate.legalHoldCheckedAt)}
            />
            <CertField label={d.approvedBy} value={dialog.certificate.approvedBy} mono />
            <CertField label={d.approvedAt} value={formatDateTime(dialog.certificate.approvedAt)} />
            <CertField label={d.destroyedBy} value={dialog.certificate.destroyedBy} mono />
            <CertField label={d.destroyedAt} value={formatDateTime(dialog.certificate.destroyedAt)} />
            <CertField label={d.reason} value={dialog.certificate.reason ?? d.none} />
          </dl>
        )}
      </Dialog>

      <Toast open={!!notice} message={notice} onClose={() => setNotice(null)} autoHideDuration={4000} />
    </div>
  );
}

function CertField({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <dt className="text-paper-500">{label}</dt>
      <dd className={mono ? 'break-all font-mono ltr text-xs' : 'text-ink-900'}>{value}</dd>
    </div>
  );
}
