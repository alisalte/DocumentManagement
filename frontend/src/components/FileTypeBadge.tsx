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
      ? 'bg-rose-50 text-rose-700'
      : ext === 'docx' || ext === 'doc'
        ? 'bg-sky-50 text-sky-700'
        : ext === 'xlsx' || ext === 'xls' || ext === 'csv'
          ? 'bg-emerald-50 text-emerald-700'
          : ext === 'png' || ext === 'jpg' || ext === 'jpeg' || ext === 'webp' || ext === 'gif'
            ? 'bg-teal-50 text-teal-700'
            : ext === 'ppt' || ext === 'pptx'
              ? 'bg-orange-50 text-orange-700'
              : 'bg-amber-50 text-amber-800';

  return (
    <span
      title={fileName ?? mimeType ?? ext}
      className={cx(
        'grid size-9 shrink-0 place-items-center rounded-lg text-[10px] font-bold uppercase tracking-wide',
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
