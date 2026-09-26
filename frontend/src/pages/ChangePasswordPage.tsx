import { useState, type FormEvent } from 'react';
import { Alert, Button, Card, TextField } from '../components/ui';
import { useSession } from '../session';
import { describeError, t } from '../strings';
import { d } from './admin/directoryStrings';

/**
 * Changing your own password. Forced after a first sign-in or an administrator's reset (the server
 * refuses everything else until it is done), and available any time from the menu. Changing it
 * ends every session, so the page signs in again with the new password.
 */
export function ChangePasswordPage({ forced = false }: { forced?: boolean }) {
  const { user, changePassword, signOut } = useSession();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (next !== repeat) {
      setError(d.passwordsDiffer);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await changePassword(current, next);
      setDone(true);
      setCurrent('');
      setNext('');
      setRepeat('');
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const heading = <h1 className="text-2xl font-bold tracking-tight text-ink-900">{d.changePassword}</h1>;

  const form = (
    <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
      {forced && (
        <Alert severity="info" className="sm:col-span-2">
          {d.mustChangeTitle}
        </Alert>
      )}
      {/* A username field helps password managers file the new password under the right account. */}
      <input type="text" name="username" autoComplete="username" value={user?.username ?? ''} readOnly hidden />
      <TextField
        label={d.currentPassword}
        type="password"
        value={current}
        onChange={(event) => setCurrent(event.target.value)}
        autoComplete="current-password"
        required
        className="sm:col-span-2"
      />
      <TextField
        label={d.newPassword}
        type="password"
        value={next}
        onChange={(event) => setNext(event.target.value)}
        autoComplete="new-password"
        helperText={d.passwordHelp}
        required
      />
      <TextField
        label={d.repeatPassword}
        type="password"
        value={repeat}
        onChange={(event) => setRepeat(event.target.value)}
        autoComplete="new-password"
        required
      />
      {error && (
        <Alert severity="error" className="sm:col-span-2">
          {error}
        </Alert>
      )}
      {done && (
        <Alert severity="success" className="sm:col-span-2">
          {d.passwordChanged}
        </Alert>
      )}
      <div className="flex flex-wrap gap-2 sm:col-span-2">
        <Button type="submit" loading={busy}>
          {d.changePassword}
        </Button>
        {forced && (
          <Button variant="outline" onClick={signOut}>
            {t.signOut}
          </Button>
        )}
      </div>
    </form>
  );

  if (forced) {
    return (
      <div className="grid min-h-screen place-items-center p-4">
        <Card className="w-full max-w-xl page-enter">
          <div className="mb-5 flex items-center gap-3">
            <span className="grid size-10 place-items-center rounded-xl bg-ink-800 text-sm font-bold text-white">ب</span>
            <p className="text-sm font-medium text-paper-500">{t.appTitle}</p>
          </div>
          <div className="space-y-4">
            {heading}
            {form}
          </div>
        </Card>
      </div>
    );
  }

  return (
    <div className="max-w-2xl space-y-5 page-enter">
      <div className="flex flex-wrap items-center justify-between gap-3">{heading}</div>
      <Card>{form}</Card>
    </div>
  );
}
