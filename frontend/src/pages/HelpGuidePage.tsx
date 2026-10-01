import { useEffect, useState } from 'react';
import { Link as RouterLink, useLocation } from 'react-router';
import { Button, cx } from '../components/ui';
import { guideIntro, guideSections, type GuideSection } from './helpGuideContent';
import { guideShotLayout, type GuideArrow } from './guideShotLayout';

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
      <header className="relative overflow-hidden rounded-3xl border border-ink-200/70 bg-gradient-to-bl from-ink-900 via-ink-800 to-ink-700 px-5 py-8 text-white sm:px-8 sm:py-10">
        <div
          className="pointer-events-none absolute -start-16 -top-20 size-64 rounded-full bg-copper-400/20 blur-3xl"
          aria-hidden
        />
        <div
          className="pointer-events-none absolute -end-10 bottom-0 size-48 rounded-full bg-white/10 blur-2xl"
          aria-hidden
        />
        <div className="relative flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
          <div className="max-w-xl space-y-3">
            <p className="text-xs font-semibold tracking-[0.2em] text-ink-200 uppercase">{guideIntro.brand}</p>
            <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">{guideIntro.title}</h1>
            <p className="text-sm leading-7 text-ink-100/90 sm:text-base">{guideIntro.lead}</p>
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
              className="!border-white/30 !bg-white/10 !text-white hover:!border-white/50 hover:!bg-white/15"
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
                        ? 'bg-ink-800 text-white shadow-[0_2px_10px_rgb(12_32_52/0.18)]'
                        : 'text-paper-600 hover:bg-ink-50 hover:text-ink-900',
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
            <span className="grid size-8 place-items-center rounded-xl bg-ink-800 text-sm font-bold text-white">
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

      <div className={cx('space-y-4 rounded-2xl border border-paper-200/90 bg-gradient-to-b p-3 sm:p-4', toneRing)}>
        {section.shots.map((shot) => (
          <GuideFigure
            key={shot.id}
            id={shot.id}
            caption={shot.caption}
            arrows={guideShotLayout[shot.id]?.arrows ?? []}
          />
        ))}
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

function GuideFigure({ id, caption, arrows }: { id: string; caption: string; arrows: GuideArrow[] }) {
  return (
    <figure className="overflow-hidden rounded-xl border border-paper-300/80 bg-white shadow-[0_8px_28px_rgb(12_32_52/0.08)]">
      <div className="relative">
        <img src={`/guide/${id}.jpg`} alt={caption} className="block w-full" />
        <div className="pointer-events-none absolute inset-0" dir="ltr" aria-hidden>
          {arrows.map((arrow) => (
            <GuideArrow key={arrow.label} arrow={arrow} />
          ))}
        </div>
      </div>
      <figcaption className="border-t border-paper-100 bg-paper-50 px-3 py-2 text-center text-xs text-paper-600">
        {caption}
      </figcaption>
    </figure>
  );
}

function GuideArrow({ arrow }: { arrow: GuideArrow }) {
  const chip =
    'inline-flex items-center gap-1 whitespace-nowrap rounded-full bg-ink-900 px-2 py-1 text-[10px] font-bold leading-none text-white shadow-[0_4px_12px_rgb(12_32_52/0.35)] ring-2 ring-white sm:text-[11px]';
  const icon = 'size-3.5 shrink-0 text-copper-400 drop-shadow';
  const place =
    arrow.from === 'top'
      ? 'flex -translate-x-1/2 -translate-y-full flex-col items-center pb-1'
      : arrow.from === 'bottom'
        ? 'flex -translate-x-1/2 flex-col items-center pt-1'
        : arrow.from === 'left'
          ? 'flex -translate-x-full -translate-y-1/2 items-center pe-1'
          : 'flex -translate-y-1/2 items-center ps-1';
  const point =
    arrow.from === 'top' ? 'rotate-180' : arrow.from === 'left' ? 'rotate-90' : arrow.from === 'right' ? '-rotate-90' : '';
  const at = { left: `${arrow.x}%`, top: `${arrow.y}%` };

  return (
    <>
      <span
        className="absolute size-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-copper-500 shadow-[0_0_0_3px_white,0_0_0_5px_rgb(217_119_6/0.45)]"
        style={at}
      />
      <span className={`absolute ${place}`} style={at}>
        {arrow.from === 'right' || arrow.from === 'bottom' ? <ArrowIcon className={`${icon} ${point}`} /> : null}
        <span className={chip}>{arrow.label}</span>
        {arrow.from === 'left' || arrow.from === 'top' ? <ArrowIcon className={`${icon} ${point}`} /> : null}
      </span>
    </>
  );
}

/** Arrow points up; callers rotate it toward the dot. */
function ArrowIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden className={className}>
      <path d="M10.5 2.4a.7.7 0 0 0-1 0L4.2 8.1a.75.75 0 1 0 1.1 1L9.25 5v11.2a.75.75 0 0 0 1.5 0V5l3.95 4.1a.75.75 0 1 0 1.1-1L10.5 2.4Z" />
    </svg>
  );
}
