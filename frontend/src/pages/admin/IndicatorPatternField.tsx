import { useEffect, useState } from 'react';
import { TextField } from '../../components/ui';
import { api } from '../../lib/api';
import { describeError } from '../../strings';
import { a } from './adminStrings';

/**
 * An indicator number pattern with a live sample, e.g. {TYPE}-{YEAR}-{SEQ:5} → CTR-1405-00001.
 * The server parses the pattern, so the sample and its errors match what saving would do.
 */
export function IndicatorPatternField({
  label,
  value,
  onChange,
  documentTypeId,
  placeholder,
  className,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  documentTypeId?: string;
  /** Shown when empty; for a type, the default pattern it falls back to. */
  placeholder?: string;
  className?: string;
}) {
  const [preview, setPreview] = useState<{ ok: boolean; text: string } | null>(null);
  const effective = value.trim() || placeholder?.trim() || '';

  useEffect(() => {
    if (!effective) {
      setPreview(null);
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(async () => {
      try {
        const result = await api.admin.previewIndicatorPattern(effective, documentTypeId);
        if (!cancelled) {
          setPreview({ ok: true, text: `${a.indicatorSample}: ${result.sample}` });
        }
      } catch (caught) {
        if (!cancelled) {
          setPreview({ ok: false, text: describeError(caught) });
        }
      }
    }, 300);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [effective, documentTypeId]);

  return (
    <div className={className}>
      <TextField
        label={label}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        dir="ltr"
        maxLength={60}
        helperText={a.indicatorPatternHelp}
      />
      {preview && (
        <p dir="auto" className={`mt-1 text-xs ${preview.ok ? 'text-ink-700' : 'text-red-700'}`}>
          {preview.text}
        </p>
      )}
    </div>
  );
}
