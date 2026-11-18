import { cx } from './ui';

/** Short extension chip colored by common office / image types. */
export function FileTypeBadge({
  fileName,
  mimeType,
  className,
}: {
  fileName?: string | null;
  mimeType?: string | null;
  className?: string;
}) {
  const ext = extensionOf(fileName, mimeType);
  const tone =
    ext === 'pdf'
      ? 'bg-rose-50 text-rose-700 ring-rose-100 dark:bg-rose-500/15 dark:text-rose-300 dark:ring-rose-500/20'
      : ext === 'docx' || ext === 'doc'
        ? 'bg-sky-50 text-sky-700 ring-sky-100 dark:bg-sky-500/15 dark:text-sky-300 dark:ring-sky-500/20'
        : ext === 'xlsx' || ext === 'xls' || ext === 'csv'
          ? 'bg-emerald-50 text-emerald-700 ring-emerald-100 dark:bg-emerald-500/15 dark:text-emerald-300 dark:ring-emerald-500/20'
          : ext === 'png' || ext === 'jpg' || ext === 'jpeg' || ext === 'webp' || ext === 'gif'
            ? 'bg-violet-50 text-violet-700 ring-violet-100 dark:bg-violet-500/15 dark:text-violet-300 dark:ring-violet-500/20'
            : ext === 'ppt' || ext === 'pptx'
              ? 'bg-orange-50 text-orange-700 ring-orange-100 dark:bg-orange-500/15 dark:text-orange-300 dark:ring-orange-500/20'
              : 'bg-slate-100 text-slate-600 ring-slate-200 dark:bg-slate-500/15 dark:text-slate-300 dark:ring-slate-500/20';

  return (
    <span
      title={fileName ?? mimeType ?? ext}
      className={cx(
        'grid size-9 shrink-0 place-items-center rounded-lg text-[10px] font-bold uppercase tracking-wide ring-1 ring-inset',
        tone,
        className,
      )}
    >
      {ext.slice(0, 4)}
    </span>
  );
}

function extensionOf(fileName?: string | null, mimeType?: string | null): string {
  const fromName = fileName?.includes('.') ? fileName.split('.').pop()?.toLowerCase() : undefined;
  if (fromName && fromName.length <= 5) return fromName;

  const mime = mimeType?.toLowerCase() ?? '';
  if (mime.includes('pdf')) return 'pdf';
  if (mime.includes('word') || mime.includes('msword')) return 'docx';
  if (mime.includes('sheet') || mime.includes('excel')) return 'xlsx';
  if (mime.includes('presentation') || mime.includes('powerpoint')) return 'pptx';
  if (mime.startsWith('image/')) return mime.split('/')[1]?.replace('jpeg', 'jpg') ?? 'img';
  if (mime.startsWith('text/')) return 'txt';
  return 'file';
}
