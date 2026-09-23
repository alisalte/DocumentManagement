import {
  Card,
  CardActionArea,
  CardContent,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import type { ReactNode } from 'react';
import type { DocumentListItem } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { formatBytes } from '../lib/format';
import { t } from '../strings';

interface Props {
  items: DocumentListItem[];
  onOpen?: (item: DocumentListItem) => void;
  /** Extra per-row content, e.g. a restore button in the recycle bin. */
  renderAction?: (item: DocumentListItem) => ReactNode;
  dateOf?: (item: DocumentListItem) => string;
  dateLabel?: string;
}

/** A table on wide screens, a stack of cards on phones: the same data, never a sideways scroll. */
export function DocumentList({ items, onOpen, renderAction, dateOf, dateLabel = t.updatedAt }: Props) {
  const theme = useTheme();
  const wide = useMediaQuery(theme.breakpoints.up('sm'));
  const dateFor = dateOf ?? ((item: DocumentListItem) => item.updatedAt);

  if (items.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        {t.noDocuments}
      </Typography>
    );
  }

  if (!wide) {
    return (
      <Stack spacing={1}>
        {items.map((item) => (
          <Card key={item.id} variant="outlined">
            <CardActionArea onClick={() => onOpen?.(item)} disabled={!onOpen}>
              <CardContent sx={{ py: 1.5 }}>
                <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                  {item.title}
                </Typography>
                <Typography variant="body2" color="text.secondary" noWrap>
                  {item.fileName} · {item.currentVersionLabel} · {formatBytes(item.fileSize)}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {formatDateTime(dateFor(item))}
                </Typography>
              </CardContent>
            </CardActionArea>
            {renderAction && <Stack sx={{ px: 2, pb: 1.5 }}>{renderAction(item)}</Stack>}
          </Card>
        ))}
      </Stack>
    );
  }

  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{t.title}</TableCell>
            <TableCell>{t.file}</TableCell>
            <TableCell>{t.version}</TableCell>
            <TableCell>{t.size}</TableCell>
            <TableCell>{dateLabel}</TableCell>
            {renderAction && <TableCell />}
          </TableRow>
        </TableHead>
        <TableBody>
          {items.map((item) => (
            <TableRow
              key={item.id}
              hover={!!onOpen}
              onClick={() => onOpen?.(item)}
              sx={{ cursor: onOpen ? 'pointer' : 'default' }}
            >
              <TableCell sx={{ fontWeight: 600 }}>{item.title}</TableCell>
              <TableCell sx={{ maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {item.fileName}
              </TableCell>
              <TableCell dir="ltr">{item.currentVersionLabel}</TableCell>
              <TableCell>{formatBytes(item.fileSize)}</TableCell>
              <TableCell>{formatDateTime(dateFor(item))}</TableCell>
              {renderAction && <TableCell onClick={(event) => event.stopPropagation()}>{renderAction(item)}</TableCell>}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
