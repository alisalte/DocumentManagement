import { useState, type FormEvent } from 'react';
import { ScannerDownload } from '../components/ScannerDownload';
import { Alert, Button, TextField } from '../components/ui';
import { useSession } from '../session';
import { describeError, t } from '../strings';

function LogoGlyph({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden>
      <path strokeLinecap="round" strokeLinejoin="round" d="M4 7.5A2.5 2.5 0 0 1 6.5 5h3.2l2 2h5.8A2.5 2.5 0 0 1 20 9.5v7a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 16.5v-9Z" />
      <path strokeLinecap="round" d="M8.5 13h7" />
    </svg>
  );
}

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
    <div className="grid min-h-screen lg:h-screen lg:grid-cols-2 lg:overflow-hidden">
      <aside className="relative hidden h-full overflow-hidden text-white lg:flex">
        <div
          className="pointer-events-none absolute inset-0"
          aria-hidden
          style={{
            backgroundImage:
              'radial-gradient(ellipse 70% 55% at 85% 0%, rgb(129 140 248 / 0.45), transparent 60%), radial-gradient(ellipse 60% 45% at 0% 100%, rgb(14 165 233 / 0.22), transparent 55%), linear-gradient(160deg, #0e1022 0%, #1e1b4b 50%, #3730a3 100%)',
          }}
        />
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.07]"
          aria-hidden
          style={{
            backgroundImage:
              'linear-gradient(rgb(255 255 255) 1px, transparent 1px), linear-gradient(90deg, rgb(255 255 255) 1px, transparent 1px)',
            backgroundSize: '44px 44px',
            maskImage: 'radial-gradient(ellipse 80% 70% at 50% 30%, black, transparent 75%)',
          }}
        />

        <div className="relative flex h-full w-full flex-col justify-between gap-8 overflow-y-auto p-12 xl:p-16">
          <div>
            <div className="mb-10 inline-flex items-center gap-3">
              <span className="grid size-11 place-items-center rounded-xl bg-white/10 shadow-lg ring-1 ring-white/20 backdrop-blur-sm">
                <LogoGlyph className="size-6" />
              </span>
              <span className="text-lg font-extrabold tracking-tight">Fillo</span>
            </div>
            <h1 className="max-w-md text-4xl font-bold leading-tight tracking-tight xl:text-5xl">{t.appTitle}</h1>
            <p className="mt-5 max-w-sm text-base leading-7 text-white/70">
              آرشیو سازمانی اسناد با نسخه‌بندی، دسترسی دقیق، گردش‌کار و سوابق فعالیت کامل.
            </p>
            <ul className="mt-8 space-y-3 text-sm text-white/80">
              {['جستجوی تمام‌متن، حتی در اسکن‌ها', 'کارتابل و گردش‌کار تأیید اسناد', 'نسخه‌بندی و سوابق قابل حسابرسی'].map((item) => (
                <li key={item} className="flex items-center gap-3">
                  <span className="grid size-6 shrink-0 place-items-center rounded-full bg-white/10 ring-1 ring-white/20">
                    <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5 text-emerald-300">
                      <path fillRule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clipRule="evenodd" />
                    </svg>
                  </span>
                  {item}
                </li>
              ))}
            </ul>
          </div>

          <ScannerDownload className="mx-auto w-full max-w-xs" />

          <p className="text-xs tracking-wide text-white/40">امن · قابل حسابرسی · فارسی</p>
        </div>
      </aside>

      <div className="relative flex min-h-screen flex-col overflow-y-auto bg-paper-100 p-6 sm:p-10 lg:h-full lg:min-h-0">
        <div className="relative m-auto w-full max-w-sm animate-[fade-in_0.4s_ease-out]">
          <div className="mb-8 text-center lg:text-start">
            <div className="mb-5 inline-flex items-center gap-2.5 lg:hidden">
              <span className="grid size-11 place-items-center rounded-xl fillo-gradient text-white shadow-[var(--shadow-brand)]">
                <LogoGlyph className="size-6" />
              </span>
              <span className="text-lg font-extrabold tracking-tight text-ink-900">Fillo</span>
            </div>
            <h2 className="text-2xl font-bold tracking-tight text-ink-900 lg:text-3xl">ورود به حساب</h2>
            <p className="mt-2 text-sm text-paper-500">نام کاربری و گذرواژهٔ سازمانی خود را وارد کنید.</p>
          </div>

          <form
            onSubmit={submit}
            className="space-y-4 rounded-2xl border border-paper-200 bg-paper-50 p-6 shadow-[var(--shadow-elevated)]"
          >
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
            <Button type="submit" fullWidth size="lg" loading={busy} className="!mt-6">
              {t.signIn}
            </Button>
          </form>
          <div className="lg:hidden">
            <ScannerDownload />
          </div>
        </div>
      </div>
    </div>
  );
}
