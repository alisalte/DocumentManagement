import { Alert, Badge, Box, Button, CircularProgress, Divider, List, ListItemButton, ListItemText, Popover, Stack, Typography } from '@mui/material';
import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { api, type AppNotification } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { describeError } from '../../strings';
import { describeNotification, n, notificationTarget } from './notificationStrings';

/**
 * The bell in the app bar: an unread count that refreshes every minute, and the latest
 * notifications in a panel. Opening one marks it read and goes where it points.
 */
export function NotificationBell() {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const unread = useQuery({ queryKey: ['notifications', 'count'], queryFn: api.notifications.unreadCount, refetchInterval: 60_000 });
  const pages = useInfiniteQuery({
    queryKey: ['notifications', 'list'],
    queryFn: ({ pageParam }) => api.notifications.list({ cursor: pageParam, take: 15 }),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
    enabled: anchor !== null,
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['notifications'] });

  const open = async (item: AppNotification) => {
    setAnchor(null);
    if (!item.readAt) {
      await api.notifications.markRead([item.id]).catch(() => undefined);
      void refresh();
    }

    const target = notificationTarget(item);
    if (target) {
      navigate(target);
    }
  };

  const markAll = async () => {
    await api.notifications.markRead().catch(() => undefined);
    void refresh();
  };

  const items = pages.data?.pages.flatMap((page) => page.items) ?? [];
  const count = unread.data?.count ?? 0;

  return (
    <>
      <Button color="inherit" onClick={(event) => setAnchor(event.currentTarget)} aria-haspopup="dialog">
        <Badge color="secondary" badgeContent={count} max={99}>
          {n.notifications}
        </Badge>
      </Button>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        transformOrigin={{ vertical: 'top', horizontal: 'left' }}
        slotProps={{ paper: { sx: { width: { xs: 'calc(100vw - 32px)', sm: 400 }, maxHeight: '70vh' } } }}
      >
        <Stack direction="row" sx={{ px: 2, py: 1, alignItems: 'center', justifyContent: 'space-between' }}>
          <Typography variant="subtitle1" component="h2">
            {n.notifications}
          </Typography>
          {count > 0 && (
            <Button size="small" onClick={markAll}>
              {n.markAllRead}
            </Button>
          )}
        </Stack>
        <Divider />
        {pages.isPending && (
          <Box sx={{ p: 2, textAlign: 'center' }}>
            <CircularProgress size={24} />
          </Box>
        )}
        {pages.isError && <Alert severity="error">{describeError(pages.error)}</Alert>}
        {pages.isSuccess && items.length === 0 && (
          <Typography sx={{ p: 2 }} color="text.secondary">
            {n.empty}
          </Typography>
        )}
        <List dense disablePadding>
          {items.map((item) => (
            <ListItemButton
              key={item.id}
              onClick={() => open(item)}
              sx={{ alignItems: 'flex-start', bgcolor: item.readAt ? undefined : 'action.hover' }}
            >
              <ListItemText
                primary={describeNotification(item)}
                secondary={formatDateTime(item.createdAt)}
                slotProps={{ primary: { sx: { fontWeight: item.readAt ? 400 : 600 } } }}
              />
            </ListItemButton>
          ))}
        </List>
        {pages.hasNextPage && (
          <Box sx={{ p: 1, textAlign: 'center' }}>
            <Button size="small" onClick={() => pages.fetchNextPage()} disabled={pages.isFetchingNextPage}>
              {n.more}
            </Button>
          </Box>
        )}
      </Popover>
    </>
  );
}
