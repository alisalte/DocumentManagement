import { useState, type FormEvent } from 'react';
import { Alert, Button, Card, TextField } from '../components/ui';
import { useSession } from '../session';
import { describeError, t } from '../strings';

export function LoginPage() {
  const { signIn } = useSession();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await signIn(username, password);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid min-h-screen place-items-center bg-slate-100 p-4">
      <Card className="w-full max-w-sm">
        <form onSubmit={submit} className="space-y-4">
          <h1 className="text-center text-xl font-bold text-slate-800">{t.appTitle}</h1>
          <TextField
            label={t.username}
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            autoComplete="username"
            autoFocus
            required
          />
          <TextField
            label={t.password}
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            required
          />
          {error && <Alert severity="error">{error}</Alert>}
          <Button type="submit" size="lg" fullWidth loading={busy}>
            {t.signIn}
          </Button>
        </form>
      </Card>
    </div>
  );
}
