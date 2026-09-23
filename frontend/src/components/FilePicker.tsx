import { Box, Button, LinearProgress, Stack, Typography } from '@mui/material';
import { useRef } from 'react';
import { formatBytes, formatNumber } from '../lib/format';
import { t } from '../strings';

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

  return (
    <Box
      sx={{
        border: 1,
        borderStyle: 'dashed',
        borderColor: 'divider',
        borderRadius: 1,
        p: 2,
        textAlign: 'center',
      }}
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => {
        event.preventDefault();
        if (!disabled && event.dataTransfer.files[0]) onChange(event.dataTransfer.files[0]);
      }}
    >
      <input
        ref={input}
        type="file"
        hidden
        onChange={(event) => onChange(event.target.files?.[0] ?? null)}
      />
      <Stack spacing={1} sx={{ alignItems: 'center' }}>
        <Button variant="outlined" onClick={() => input.current?.click()} disabled={disabled}>
          {t.chooseFile}
        </Button>
        {file && (
          <Typography variant="body2" sx={{ wordBreak: 'break-all' }}>
            {file.name} — {formatBytes(file.size)}
          </Typography>
        )}
        {progress !== null && (
          <Box sx={{ width: '100%' }}>
            <LinearProgress variant="determinate" value={progress * 100} />
            <Typography variant="caption" color="text.secondary">
              {t.uploading} {formatNumber(Math.round(progress * 100))}٪
            </Typography>
          </Box>
        )}
      </Stack>
    </Box>
  );
}
