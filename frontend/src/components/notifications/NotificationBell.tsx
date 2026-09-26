import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { api, type AppNotification } from '../../lib/api';
import { formatDateTime } from '../../lib/dates';
import { describeError } from '../../strings';
import { Alert, Badge, Button, Menu, Spinner, cx } from '../ui';
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
      <button
        type="button"
        aria-label={n.notifications}
        title={n.notifications}
        onClick={(event) => setAnchor(event.currentTarget)}
        aria-haspopup="dialog"
        className="relative inline-flex size-9 items-center justify-center rounded-xl text-paper-500 transition-colors hover:bg-ink-50 hover:text-ink-800"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden className="size-5">
          <path strokeLinecap="round" strokeLinejoin="round" d="M14.86 17.082a23.85 23.85 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 0a3 3 0 1 1-5.714 0" />
        </svg>
        <Badge count={count} max={99} />
      </button>

      <Menu anchor={anchor} onClose={() => setAnchor(null)} className="w-[calc(100vw-1.5rem)] sm:w-96">
        <div className="flex items-center justify-between gap-3 border-b border-paper-100 px-3 py-2">
          <h2 className="text-sm font-semibold text-ink-800">{n.notifications}</h2>
          {count > 0 && (
            <Button variant="ghost" size="sm" onClick={markAll}>
              {n.markAllRead}
            </Button>
          )}
        </div>

        {pages.isPending && (
          <div className="grid place-items-center py-6">
            <Spinner size="sm" />
          </div>
        )}
        {pages.isError && (
          <div className="p-2">
            <Alert severity="error">{describeError(pages.error)}</Alert>
          </div>
        )}
        {pages.isSuccess && items.length === 0 && <p className="px-3 py-3 text-sm text-paper-500">{n.empty}</p>}

        {items.map((item) => (
          <button
            key={item.id}
            type="button"
            onClick={() => open(item)}
            className={cx(
              'flex w-full flex-col gap-0.5 px-3 py-2.5 text-start transition-colors hover:bg-paper-50',
              item.readAt ? 'text-paper-600' : 'bg-ink-50/60 font-semibold text-ink-900',
            )}
          >
            <span className="text-sm leading-6">{describeNotification(item)}</span>
            <span className="text-xs font-normal text-paper-400">{formatDateTime(item.createdAt)}</span>
          </button>
        ))}

        {pages.hasNextPage && (
          <div className="border-t border-paper-100 p-2 text-center">
            <Button variant="ghost" size="sm" loading={pages.isFetchingNextPage} onClick={() => pages.fetchNextPage()}>
              {n.more}
            </Button>
          </div>
        )}
      </Menu>
    </>
  );
}
