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
      <aside className="relative hidden overflow-hidden bg-ink-700 text-white lg:flex lg:flex-col lg:justify-between lg:p-12 xl:p-16">
        <div
          className="pointer-events-none absolute inset-0"
          aria-hidden
          style={{
            backgroundImage:
              'radial-gradient(ellipse 80% 60% at 15% 5%, rgb(255 203 51 / 0.55), transparent 55%), radial-gradient(ellipse 70% 50% at 95% 90%, rgb(255 122 64 / 0.4), transparent 50%), linear-gradient(165deg, #d95428 0%, #c0441c 42%, #9a3618 100%)',
          }}
        />
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.08]"
          aria-hidden
          style={{
            backgroundImage:
              'url("data:image/svg+xml,%3Csvg width=\'60\' height=\'60\' viewBox=\'0 0 60 60\' xmlns=\'http://www.w3.org/2000/svg\'%3E%3Cg fill=\'none\' fill-rule=\'evenodd\'%3E%3Cg fill=\'%23ffffff\' fill-opacity=\'1\'%3E%3Cpath d=\'M36 34v-4h-2v4h-4v2h4v4h2v-4h4v-2h-4zm0-30V0h-2v4h-4v2h4v4h2V6h4V4h-4zM6 34v-4H4v4H0v2h4v4h2v-4h4v-2H6zM6 4V0H4v4H0v2h4v4h2V6h4V4H6z\'/%3E%3C/g%3E%3C/g%3E%3C/svg%3E")',
          }}
        />

        <div className="relative">
          <div className="mb-10 inline-flex items-center gap-3">
            <span className="grid size-12 place-items-center rounded-2xl bg-copper-400/90 text-xl font-bold text-ink-950 shadow-[0_4px_16px_rgb(245_180_15/0.35)] ring-1 ring-white/30">
              ب
            </span>
            <span className="text-sm font-medium tracking-[0.2em] text-white/80 uppercase">DMS</span>
          </div>
          <h1 className="max-w-md text-4xl font-bold leading-tight tracking-tight xl:text-5xl">{t.appTitle}</h1>
          <p className="mt-5 max-w-sm text-base leading-7 text-white/85">
            آرشیو سازمانی اسناد با نسخه‌بندی، دسترسی دقیق، گردش‌کار و رویدادنگاری کامل.
          </p>
        </div>

        <p className="relative text-xs tracking-wide text-white/55">امن · قابل حسابرسی · فارسی</p>
      </aside>

      <div className="relative flex items-center justify-center p-6 sm:p-10">
        <div className="absolute inset-0 lg:hidden" aria-hidden>
          <div className="absolute inset-0 bg-gradient-to-b from-ink-500 via-ink-600 to-paper-100 opacity-95" />
        </div>

        <div className="relative w-full max-w-sm animate-[fade-in_0.4s_ease-out]">
          <div className="mb-8 text-center lg:text-start">
            <div className="mb-5 inline-flex items-center gap-2.5 lg:hidden">
              <span className="grid size-11 place-items-center rounded-2xl bg-copper-400 text-lg font-bold text-ink-950 shadow-lg ring-1 ring-white/40">
                ب
              </span>
            </div>
            <p className="mb-2 text-[11px] font-semibold tracking-[0.18em] text-white/80 uppercase lg:text-paper-500">
              ورود به سامانه
            </p>
            <h2 className="text-2xl font-bold tracking-tight text-white lg:text-3xl lg:text-ink-900">{t.appTitle}</h2>
            <p className="mt-2 text-sm text-white/75 lg:text-paper-500">نام کاربری و گذرواژهٔ سازمانی خود را وارد کنید.</p>
          </div>

          <form
            onSubmit={submit}
            className="space-y-4 rounded-2xl border border-paper-200/90 bg-white/95 p-5 shadow-[0_1px_2px_rgb(77_29_16/0.05),0_14px_36px_rgb(217_84_40/0.12)] backdrop-blur-sm sm:p-6"
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
