import { useRef } from 'react';
import { formatBytes, formatNumber } from '../lib/format';
import { t } from '../strings';
import { Button } from './ui';

interface Props {
  file: File | null;
  onChange: (file: File | null) => void;
  /** 0..1 while uploading, null otherwise. */
  progress: number | null;
  disabled?: boolean;
}

/**
 * A plain file input behind a large button. On a phone the same input offers the camera and
 * the file browser, which is how most scanned paperwork will arrive.
 */
export function FilePicker({ file, onChange, progress, disabled }: Props) {
  const input = useRef<HTMLInputElement>(null);
  const percent = progress === null ? 0 : Math.round(progress * 100);

  return (
    <div
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => {
        event.preventDefault();
        if (!disabled && event.dataTransfer.files[0]) onChange(event.dataTransfer.files[0]);
      }}
      className="rounded-2xl border-2 border-dashed border-paper-300 bg-gradient-to-b from-white/80 to-paper-50/80 p-5 text-center transition-colors hover:border-ink-300"
    >
      <input
        ref={input}
        type="file"
        hidden
        onChange={(event) => onChange(event.target.files?.[0] ?? null)}
      />
      <div className="flex flex-col items-center gap-2">
        <Button variant="outline" onClick={() => input.current?.click()} disabled={disabled}>
          {t.chooseFile}
        </Button>

        {file && (
          <p className="text-sm break-all text-ink-800">
            {file.name} — {formatBytes(file.size)}
          </p>
        )}

        {progress !== null && (
          <div className="w-full">
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
              {t.uploading} {formatNumber(percent)}٪
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
