import { useState, type FormEvent } from 'react';
import { Alert, Button, TextField } from '../components/ui';
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
    <div className="grid min-h-screen lg:grid-cols-2">
      <aside className="relative hidden overflow-hidden bg-paper-100 text-white lg:flex lg:flex-col lg:justify-between lg:p-12 xl:p-16">
        <div
          className="pointer-events-none absolute inset-0"
          aria-hidden
          style={{
            backgroundImage:
              'radial-gradient(ellipse 80% 60% at 20% 10%, rgb(124 58 237 / 0.35), transparent 55%), radial-gradient(ellipse 70% 50% at 90% 90%, rgb(249 115 22 / 0.18), transparent 50%), linear-gradient(165deg, #0a0a0c 0%, #141418 55%, #1c1c22 100%)',
          }}
        />

        <div className="relative">
          <div className="mb-10 inline-flex items-center gap-3">
            <span className="grid size-12 place-items-center rounded-2xl fillo-gradient text-xl font-bold shadow-[0_2px_16px_rgb(124_58_237/0.45)]">
              F
            </span>
            <span className="text-sm font-medium tracking-[0.2em] text-paper-500 uppercase">Fillo</span>
          </div>
          <h1 className="max-w-md text-4xl font-bold leading-tight tracking-tight text-ink-900 xl:text-5xl">{t.appTitle}</h1>
          <p className="mt-5 max-w-sm text-base leading-7 text-paper-600">
            آرشیو سازمانی اسناد با نسخه‌بندی، دسترسی دقیق، گردش‌کار و سوابق فعالیت کامل.
          </p>
        </div>

        <p className="relative text-xs tracking-wide text-paper-500">امن · قابل حسابرسی · فارسی</p>
      </aside>

      <div className="relative flex items-center justify-center bg-paper-100 p-6 sm:p-10">
        <div className="relative w-full max-w-sm animate-[fade-in_0.4s_ease-out]">
          <div className="mb-8 text-center lg:text-start">
            <div className="mb-5 inline-flex items-center gap-2.5 lg:hidden">
              <span className="grid size-11 place-items-center rounded-2xl fillo-gradient text-lg font-bold text-white shadow-lg">
                F
              </span>
              <span className="text-lg font-bold text-ink-900">Fillo</span>
            </div>
            <p className="mb-2 text-[11px] font-semibold tracking-[0.18em] text-paper-500 uppercase">
              ورود به سامانه
            </p>
            <h2 className="text-2xl font-bold tracking-tight text-ink-900 lg:text-3xl">{t.appTitle}</h2>
            <p className="mt-2 text-sm text-paper-500">نام کاربری و گذرواژهٔ سازمانی خود را وارد کنید.</p>
          </div>

          <form onSubmit={submit} className="space-y-4 rounded-2xl border border-paper-300/70 bg-paper-200/80 p-5 shadow-[0_8px_24px_rgb(0_0_0/0.25)]">
            {error && <Alert severity="error">{error}</Alert>}
            <TextField
              label={t.username}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoComplete="username"
              required
            />
            <TextField
              label={t.password}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
            <Button type="submit" fullWidth loading={busy}>
              {t.signIn}
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
}
