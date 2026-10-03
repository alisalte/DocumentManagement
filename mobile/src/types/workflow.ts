export type WorkflowAction = 'Approve' | 'Reject' | 'Return' | 'RequestChanges' | 'Forward';

export interface WorkflowTask {
  id: string;
  instanceId: string;
  documentId: string;
  documentTitle: string;
  versionId: string;
  versionLabel: string;
  stepCode: string;
  stepName: string;
  status: 'Pending' | 'Completed' | 'Cancelled';
  createdAt: string;
  dueAt: string | null;
  allowedActions: WorkflowAction[];
  canAct: boolean;
  isOverdue: boolean;
}
