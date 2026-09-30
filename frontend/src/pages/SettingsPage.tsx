import { Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import { Alert, Card, Tab, Tabs } from '../components/ui';
import { useSession } from '../session';
import { adminSections } from './admin/sections';

/**
 * Administration lives here, one screen per tab. Editors (a workflow, a document type) stay on
 * the same tab as their list.
 */
export function SettingsLayout() {
  const { user } = useSession();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const tabs = adminSections(user).filter((section) => section.allowed);
  const active =
    tabs.find((section) => pathname === `/settings/${section.id}` || pathname.startsWith(`/settings/${section.id}/`))
      ?.id ?? '';

  return (
    <div className="space-y-4">
      {tabs.length === 0 ? (
        <Card>
          <Alert severity="info">بخش مدیریتی برای حساب شما فعال نیست.</Alert>
        </Card>
      ) : (
        <>
          <Tabs value={active} onChange={(id) => navigate(`/settings/${id}`)} className="overflow-x-auto">
            {tabs.map((section) => (
              <Tab key={section.id} value={section.id} label={section.label} />
            ))}
          </Tabs>
          <Outlet />
        </>
      )}
    </div>
  );
}

/** `/settings` opens the first section this account may use. */
export function SettingsHome() {
  const { user } = useSession();
  const first = adminSections(user).find((section) => section.allowed);
  if (!first) return null;
  return <Navigate to={`/settings/${first.id}`} replace />;
}
