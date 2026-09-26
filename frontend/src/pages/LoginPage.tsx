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
      <aside className="relative hidden overflow-hidden bg-ink-900 text-white lg:flex lg:flex-col lg:justify-between lg:p-12 xl:p-16">
        <div
          className="pointer-events-none absolute inset-0"
          aria-hidden
          style={{
            backgroundImage:
              'radial-gradient(ellipse 80% 60% at 20% 10%, rgb(79 122 168 / 0.45), transparent 55%), radial-gradient(ellipse 70% 50% at 90% 90%, rgb(150 104 67 / 0.22), transparent 50%), linear-gradient(165deg, #0c2034 0%, #163a5c 55%, #112d48 100%)',
          }}
        />
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.07]"
          aria-hidden
          style={{
            backgroundImage:
              'url("data:image/svg+xml,%3Csvg width=\'60\' height=\'60\' viewBox=\'0 0 60 60\' xmlns=\'http://www.w3.org/2000/svg\'%3E%3Cg fill=\'none\' fill-rule=\'evenodd\'%3E%3Cg fill=\'%23ffffff\' fill-opacity=\'1\'%3E%3Cpath d=\'M36 34v-4h-2v4h-4v2h4v4h2v-4h4v-2h-4zm0-30V0h-2v4h-4v2h4v4h2V6h4V4h-4zM6 34v-4H4v4H0v2h4v4h2v-4h4v-2H6zM6 4V0H4v4H0v2h4v4h2V6h4V4H6z\'/%3E%3C/g%3E%3C/g%3E%3C/svg%3E")',
          }}
        />

        <div className="relative">
          <div className="mb-10 inline-flex items-center gap-3">
            <span className="grid size-12 place-items-center rounded-2xl bg-white/10 text-xl font-bold backdrop-blur-sm ring-1 ring-white/20">
              ب
            </span>
            <span className="text-sm font-medium tracking-[0.2em] text-white/70 uppercase">DMS</span>
          </div>
          <h1 className="max-w-md text-4xl font-bold leading-tight tracking-tight xl:text-5xl">{t.appTitle}</h1>
          <p className="mt-5 max-w-sm text-base leading-7 text-white/70">
            آرشیو سازمانی اسناد با نسخه‌بندی، دسترسی دقیق، گردش‌کار و رویدادنگاری کامل.
          </p>
        </div>

        <p className="relative text-xs tracking-wide text-white/40">امن · قابل حسابرسی · فارسی</p>
      </aside>

      <div className="relative flex items-center justify-center p-6 sm:p-10">
        <div className="absolute inset-0 lg:hidden" aria-hidden>
          <div className="absolute inset-0 bg-gradient-to-b from-ink-900 via-ink-800 to-paper-100 opacity-95" />
        </div>

        <div className="relative w-full max-w-sm animate-[fade-in_0.4s_ease-out]">
          <div className="mb-8 text-center lg:text-start">
            <div className="mb-5 inline-flex items-center gap-2.5 lg:hidden">
              <span className="grid size-11 place-items-center rounded-2xl bg-white/15 text-lg font-bold text-white shadow-lg ring-1 ring-white/25">
                ب
              </span>
            </div>
            <p className="mb-2 text-[11px] font-semibold tracking-[0.18em] text-white/70 uppercase lg:text-paper-500">
              ورود به سامانه
            </p>
            <h2 className="text-2xl font-bold tracking-tight text-white lg:text-3xl lg:text-ink-900">{t.appTitle}</h2>
            <p className="mt-2 text-sm text-white/65 lg:text-paper-500">نام کاربری و گذرواژهٔ سازمانی خود را وارد کنید.</p>
          </div>

          <form
            onSubmit={submit}
            className="space-y-4 rounded-2xl border border-paper-200/90 bg-white/90 p-5 shadow-[0_1px_2px_rgb(12_32_52/0.04),0_12px_32px_rgb(12_32_52/0.08)] backdrop-blur-sm sm:p-6"
          >
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
        </div>
      </div>
    </div>
  );
}
