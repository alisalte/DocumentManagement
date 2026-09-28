import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink, useLocation } from 'react-router';
import { Button, cx } from '../components/ui';
import { guideIntro, guideSections, type GuideSection } from './helpGuideContent';

/**
 * In-app graphical how-to: Persian, RTL, illustrated mocks of the real chrome
 * so someone who has never used the archive can follow the main paths.
 */
export function HelpGuidePage() {
  const location = useLocation();
  const [active, setActive] = useState(guideSections[0]?.id ?? 'start');

  useEffect(() => {
    const hash = location.hash.replace(/^#/, '');
    if (hash && guideSections.some((section) => section.id === hash)) {
      setActive(hash);
      document.getElementById(hash)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [location.hash]);

  useEffect(() => {
    const nodes = guideSections
      .map((section) => document.getElementById(section.id))
      .filter((node): node is HTMLElement => !!node);
    if (nodes.length === 0) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
        if (visible?.target.id) setActive(visible.target.id);
      },
      { rootMargin: '-20% 0px -55% 0px', threshold: [0.15, 0.4, 0.7] },
    );
    for (const node of nodes) observer.observe(node);
    return () => observer.disconnect();
  }, []);

  return (
    <div className="mx-auto max-w-5xl space-y-8 page-enter">
      <header className="relative overflow-hidden rounded-3xl border border-ink-300/50 bg-gradient-to-bl from-ink-500 via-ink-600 to-copper-500 px-5 py-8 text-white sm:px-8 sm:py-10">
        <div
          className="pointer-events-none absolute -start-16 -top-20 size-64 rounded-full bg-copper-300/45 blur-3xl"
          aria-hidden
        />
        <div
          className="pointer-events-none absolute -end-10 bottom-0 size-48 rounded-full bg-white/20 blur-2xl"
          aria-hidden
        />
        <div className="relative flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
          <div className="max-w-xl space-y-3">
            <p className="text-xs font-semibold tracking-[0.2em] text-copper-100 uppercase">{guideIntro.brand}</p>
            <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">{guideIntro.title}</h1>
            <p className="text-sm leading-7 text-white/90 sm:text-base">{guideIntro.lead}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button as={RouterLink} to="/new" variant="secondary" size="sm">
              ثبت سند
            </Button>
            <Button
              as={RouterLink}
              to="/search"
              variant="outline"
              size="sm"
              className="!border-white/40 !bg-white/15 !text-white hover:!border-white/60 hover:!bg-white/25"
            >
              جستجو
            </Button>
          </div>
        </div>
      </header>

      <div className="flex flex-col gap-8 lg:flex-row">
        <nav
          aria-label={guideIntro.tocLabel}
          className="lg:sticky lg:top-24 lg:w-56 lg:shrink-0 lg:self-start"
        >
          <p className="section-label pb-3">{guideIntro.tocLabel}</p>
          <ol className="flex gap-2 overflow-x-auto pb-1 lg:flex-col lg:gap-0.5 lg:overflow-visible lg:pb-0">
            {guideSections.map((section, index) => {
              const selected = active === section.id;
              return (
                <li key={section.id} className="shrink-0">
                  <a
                    href={`#${section.id}`}
                    onClick={() => setActive(section.id)}
                    className={cx(
                      'flex items-center gap-2 rounded-xl px-3 py-2 text-sm transition-all duration-200',
                      selected
                        ? 'bg-ink-600 text-white shadow-[0_2px_10px_rgb(217_84_40/0.22)]'
                        : 'text-paper-600 hover:bg-copper-50 hover:text-ink-900',
                    )}
                  >
                    <span
                      className={cx(
                        'grid size-6 place-items-center rounded-lg text-[11px] font-bold',
                        selected ? 'bg-white/15' : 'bg-paper-100 text-paper-500',
                      )}
                    >
                      {(index + 1).toLocaleString('fa-IR')}
                    </span>
                    <span className="whitespace-nowrap font-medium">{section.title}</span>
                  </a>
                </li>
              );
            })}
          </ol>
        </nav>

        <div className="min-w-0 flex-1 space-y-10">
          {guideSections.map((section, index) => (
            <GuideBlock key={section.id} section={section} index={index} />
          ))}
        </div>
      </div>
    </div>
  );
}

function GuideBlock({ section, index }: { section: GuideSection; index: number }) {
  const toneRing =
    section.tone === 'copper'
      ? 'from-copper-100/80 to-white'
      : section.tone === 'paper'
        ? 'from-paper-100 to-white'
        : 'from-ink-50 to-white';

  return (
    <section
      id={section.id}
      className="scroll-mt-28 space-y-5 border-t border-paper-200/80 pt-8 first:border-t-0 first:pt-0"
      aria-labelledby={`${section.id}-title`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="max-w-2xl space-y-2">
          <div className="flex items-center gap-2.5">
            <span className="grid size-8 place-items-center rounded-xl bg-ink-600 text-sm font-bold text-white">
              {(index + 1).toLocaleString('fa-IR')}
            </span>
            <h2 id={`${section.id}-title`} className="text-xl font-bold tracking-tight text-ink-900 sm:text-2xl">
              {section.title}
            </h2>
          </div>
          <p className="text-sm leading-7 text-paper-600 sm:text-[15px]">{section.summary}</p>
        </div>
        {section.tryHref && (
          <Button as={RouterLink} to={section.tryHref} variant="outline" size="sm" className="shrink-0">
            {section.tryLabel ?? 'امتحان کنید'}
            <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className="size-3.5 -scale-x-100 opacity-70">
              <path
                fillRule="evenodd"
                d="M3 10a.75.75 0 0 1 .75-.75h10.638L10.23 5.29a.75.75 0 1 1 1.04-1.08l5.5 5.25a.75.75 0 0 1 0 1.08l-5.5 5.25a.75.75 0 1 1-1.04-1.08l4.158-3.96H3.75A.75.75 0 0 1 3 10Z"
                clipRule="evenodd"
              />
            </svg>
          </Button>
        )}
      </div>

      <div className={cx('overflow-hidden rounded-2xl border border-paper-200/90 bg-gradient-to-b p-3 sm:p-4', toneRing)}>
        <UiMock section={section} />
        <p className="mt-3 text-center text-xs text-paper-500">{section.mock.caption}</p>
      </div>

      <ol className="space-y-3">
        {section.steps.map((step, stepIndex) => (
          <li
            key={step.title}
            className="guide-step flex gap-3 rounded-2xl border border-transparent px-1 py-1 transition-colors duration-200 hover:border-paper-200/80 hover:bg-white/70"
            style={{ animationDelay: `${stepIndex * 40}ms` }}
          >
            <span className="mt-0.5 grid size-7 shrink-0 place-items-center rounded-full bg-ink-100 text-xs font-bold text-ink-700">
              {(stepIndex + 1).toLocaleString('fa-IR')}
            </span>
            <div className="min-w-0">
              <p className="font-semibold text-ink-900">{step.title}</p>
              <p className="mt-0.5 text-sm leading-7 text-paper-600">{step.body}</p>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}

function UiMock({ section }: { section: GuideSection }) {
  const spots = new Set(section.mock.highlights.map((item) => item.spot));
  const label = (spot: GuideSection['mock']['highlights'][number]['spot']) =>
    section.mock.highlights.find((item) => item.spot === spot)?.label;

  return (
    <div className="overflow-hidden rounded-xl border border-paper-300/80 bg-white shadow-[0_8px_28px_rgb(217_84_40/0.1)]">
      <div className="flex items-center gap-1.5 border-b border-paper-100 bg-paper-50 px-3 py-2">
        <span className="size-2 rounded-full bg-paper-300" />
        <span className="size-2 rounded-full bg-paper-300" />
        <span className="size-2 rounded-full bg-paper-300" />
        <span className="ms-2 truncate text-[11px] text-paper-400">بایگانی اسناد — {section.title}</span>
      </div>

      <div className="grid gap-2 p-3 sm:grid-cols-[7.5rem_1fr]">
        <div
          className={cx(
            'hidden space-y-2 rounded-lg border border-dashed p-2 sm:block',
            spots.has('sidebar') ? 'border-copper-300 bg-copper-50/80 ring-2 ring-copper-300/40' : 'border-paper-200 bg-paper-50/60',
          )}
        >
          <div className="h-2 w-12 rounded bg-paper-300/80" />
          <div className="space-y-1.5">
            <div className="h-2 w-full rounded bg-ink-200/70" />
            <div className="h-2 w-4/5 rounded bg-paper-200" />
            <div className="h-2 w-3/5 rounded bg-paper-200" />
          </div>
          {spots.has('sidebar') && <Callout>{label('sidebar')}</Callout>}
        </div>

        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <div
              className={cx(
                'flex h-8 flex-1 items-center gap-2 rounded-lg border px-2',
                spots.has('search')
                  ? 'border-ink-400 bg-ink-50 ring-2 ring-ink-400/25'
                  : 'border-paper-200 bg-paper-50',
              )}
            >
              <span className="size-3 rounded-full border border-paper-400" />
              <span className="h-1.5 w-24 rounded bg-paper-300" />
              {spots.has('search') && <Callout className="ms-auto">{label('search')}</Callout>}
            </div>
            <div
              className={cx(
                'flex h-8 items-center rounded-lg px-2.5 text-[11px] font-semibold',
                spots.has('nav') ? 'bg-ink-600 text-white ring-2 ring-ink-400/40' : 'bg-paper-100 text-paper-600',
              )}
            >
              {label('nav') ?? 'نوار بالا'}
            </div>
            <div
              className={cx(
                'flex h-8 items-center rounded-lg px-2.5 text-[11px] font-semibold',
                spots.has('action')
                  ? 'bg-copper-500 text-ink-950 ring-2 ring-copper-300/50'
                  : 'bg-ink-600 text-white/95',
              )}
            >
              {label('action') ?? 'اقدام'}
            </div>
          </div>

          <div
            className={cx(
              'min-h-[7.5rem] rounded-lg border p-3',
              spots.has('viewer')
                ? 'border-ink-300 bg-gradient-to-b from-ink-50 to-white ring-2 ring-ink-300/30'
                : spots.has('list')
                  ? 'border-copper-200 bg-copper-50/40 ring-2 ring-copper-200/50'
                  : 'border-paper-200 bg-paper-50/50',
            )}
          >
            {spots.has('viewer') ? (
              <div className="space-y-2">
                <div className="mx-auto h-16 w-[70%] rounded-md border border-ink-100 bg-white shadow-sm" />
                <div className="mx-auto h-2 w-1/3 rounded bg-paper-300" />
                <Callout>{label('viewer')}</Callout>
              </div>
            ) : (
              <div className="space-y-2">
                {[0, 1, 2].map((row) => (
                  <div key={row} className="flex items-center gap-2 rounded-md bg-white/90 px-2 py-2 shadow-sm">
                    <span className="size-6 rounded-md bg-ink-100" />
                    <span className="h-2 flex-1 rounded bg-paper-200" />
                    <span className="h-2 w-10 rounded bg-paper-100" />
                  </div>
                ))}
                {(spots.has('list') || spots.has('action')) && (
                  <Callout>{label('list') ?? label('action')}</Callout>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function Callout({ children, className }: { children: ReactNode; className?: string }) {
  if (!children) return null;
  return (
    <span
      className={cx(
        'inline-flex max-w-full items-center rounded-full bg-ink-700 px-2.5 py-1 text-[10px] font-semibold tracking-wide text-white shadow-sm',
        'animate-[fade-in_0.45s_ease-out_both]',
        className,
      )}
    >
      {children}
    </span>
  );
}
