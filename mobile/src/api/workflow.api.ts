import { http, withApi } from './client';
import type { WorkflowTask } from '../types/workflow';

export function listTasks(): Promise<WorkflowTask[]> {
  return withApi(async () => (await http.get<WorkflowTask[]>('/workflow/tasks')).data);
}
