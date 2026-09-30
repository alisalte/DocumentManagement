import type { CurrentUser } from '../../lib/api';
import { t } from '../../strings';
import { w } from '../../components/workflow/workflowStrings';
import { a as audit } from './auditStrings';
import { d as directory } from './directoryStrings';
import { d as disposition } from './dispositionStrings';
import { i as importStrings } from './importStrings';

export interface AdminSection {
  id: string;
  label: string;
  allowed: boolean;
}

/** Administration screens, in the order they appear as settings tabs. */
export function adminSections(user: CurrentUser | null): AdminSection[] {
  const has = (code: string) => !!user && (user.isSystemAdmin || user.systemPermissions.includes(code));
  return [
    { id: 'users', label: directory.users, allowed: has('ADMIN_MANAGE_USERS') },
    { id: 'groups', label: directory.groups, allowed: has('ADMIN_MANAGE_GROUPS') },
    { id: 'roles', label: directory.roles, allowed: has('ADMIN_MANAGE_ROLES') },
    { id: 'categories', label: directory.categories, allowed: has('ADMIN_MANAGE_CATEGORIES') },
    { id: 'document-types', label: t.documentTypes, allowed: has('ADMIN_MANAGE_DOCUMENT_TYPES') },
    { id: 'workflows', label: w.workflows, allowed: has('ADMIN_MANAGE_WORKFLOWS') },
    { id: 'search', label: t.searchAdmin, allowed: has('ADMIN_MANAGE_SEARCH') },
    { id: 'audit', label: audit.menu, allowed: has('AUDIT_VIEW') },
    {
      id: 'disposition',
      label: disposition.menu,
      allowed:
        has('DISPOSITION_REQUEST') ||
        has('DISPOSITION_APPROVE') ||
        has('DISPOSITION_DESTROY') ||
        has('DISPOSITION_VIEW_CERTIFICATE') ||
        has('ADMIN_MANAGE_RECORDS'),
    },
    {
      id: 'imports',
      label: importStrings.menu,
      allowed: has('IMPORT_VIEW') || has('IMPORT_RUN') || has('IMPORT_MANAGE'),
    },
  ];
}
