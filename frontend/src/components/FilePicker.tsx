import { useRef, useState } from 'react';
import { formatBytes, formatNumber } from '../lib/format';
import { t } from '../strings';
import { FileTypeBadge } from './FileTypeBadge';
import { Button, cx } from './ui';

interface Props {
  file: File | null;
  /** A file already staged (quick upload) so the form can register it without picking it again. */
  staged?: { name: string; size: number } | null;
  onChange: (file: File | null) => void;
  /** 0..1 while uploading, null otherwise. */
  progress: number | null;
  disabled?: boolean;
}

/**
 * A plain file input behind a large button. On a phone the same input offers the camera and
 * the file browser, which is how most scanned paperwork will arrive.
 */
export function FilePicker({ file, staged, onChange, progress, disabled }: Props) {
  const input = useRef<HTMLInputElement>(null);
  const percent = progress === null ? 0 : Math.round(progress * 100);
  const [dragOver, setDragOver] = useState(false);
  const chosen = file ? { name: file.name, size: file.size, type: file.type } : staged ? { ...staged, type: '' } : null;

  return (
    <div
      onDragOver={(event) => {
        event.preventDefault();
        if (!disabled) setDragOver(true);
      }}
      onDragLeave={() => setDragOver(false)}
      onDrop={(event) => {
        event.preventDefault();
        setDragOver(false);
        if (!disabled && event.dataTransfer.files[0]) onChange(event.dataTransfer.files[0]);
      }}
      className={cx(
        'rounded-2xl border-2 border-dashed p-5 text-center transition-colors',
        dragOver ? 'border-ink-400 bg-ink-50' : 'border-paper-300 bg-paper-100/40 hover:border-ink-300',
      )}
    >
      <input
        ref={input}
        type="file"
        hidden
        onChange={(event) => onChange(event.target.files?.[0] ?? null)}
      />
      <div className="flex flex-col items-center gap-3">
        {chosen ? (
          <div className="flex w-full max-w-md items-center gap-3 rounded-xl border border-paper-200 bg-paper-50 p-3 text-start shadow-[var(--shadow-surface)]">
            <FileTypeBadge fileName={chosen.name} mimeType={chosen.type} />
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-ink-900">
                <bdi>{chosen.name}</bdi>
              </p>
              <p className="text-xs text-paper-500 tabular-nums">{formatBytes(chosen.size)}</p>
            </div>
            <Button variant="ghost" size="sm" onClick={() => input.current?.click()} disabled={disabled}>
              تغییر
            </Button>
          </div>
        ) : (
          <>
            <span className="grid size-12 place-items-center rounded-2xl bg-paper-50 text-ink-600 shadow-[var(--shadow-surface)] ring-1 ring-paper-200">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6} aria-hidden className="size-6">
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 16.5V9.75m0 0 3 3m-3-3-3 3M6.75 19.5a4.5 4.5 0 0 1-1.41-8.775 5.25 5.25 0 0 1 10.233-2.33 4.5 4.5 0 0 1 4.5 4.5v.008H19.5a3 3 0 0 1 3 3v1.5a3 3 0 0 1-3 3H6.75Z"
                />
              </svg>
            </span>
            <div>
              <p className="text-sm font-medium text-ink-900">فایل را اینجا رها کنید</p>
              <p className="mt-0.5 text-xs text-paper-500">یا از دستگاه انتخاب کنید؛ روی گوشی دوربین هم در دسترس است.</p>
            </div>
            <Button variant="outline" onClick={() => input.current?.click()} disabled={disabled}>
              {t.chooseFile}
            </Button>
          </>
        )}

        {progress !== null && (
          <div className="w-full max-w-md">
            <div
              role="progressbar"
              aria-label={t.uploading}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={percent}
              className="h-1.5 w-full overflow-hidden rounded-full bg-paper-200"
            >
              <div
                className="h-full rounded-full bg-ink-600 transition-[width] duration-300"
                style={{ width: `${percent}%` }}
              />
            </div>
            <p className="mt-1.5 text-xs text-paper-500">
              {percent >= 100 ? t.uploadFinishing : `${t.uploading} ${formatNumber(percent)}٪`}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
