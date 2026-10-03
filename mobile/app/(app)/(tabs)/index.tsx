import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { listCategories, listDocuments } from '../../../src/api/documents.api';
import { isDemoPreview } from '../../../src/config/demo';
import { Text } from '../../../src/components/AppText';
import { DocumentRow } from '../../../src/components/DocumentRow';
import { EmptyState } from '../../../src/components/EmptyState';
import { ErrorMessage } from '../../../src/components/ErrorMessage';
import { Input } from '../../../src/components/Input';
import { DottedFill } from '../../../src/components/PaperBackground';
import { PageHeader } from '../../../src/components/PageHeader';
import { colors, radius, space } from '../../../src/theme';
import { faDigits } from '../../../src/utils/format';
import { userMessage } from '../../../src/utils/errors';

const pageSize = 20;

export default function ArchiveScreen() {
  const router = useRouter();
  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const demo = isDemoPreview();
  const categories = useQuery({ queryKey: ['categories'], queryFn: listCategories, enabled: !demo });
  const viewable = useMemo(
    () => (categories.data ?? []).filter((category) => category.isActive && category.canView !== false),
    [categories.data],
  );

  const documents = useInfiniteQuery({
    queryKey: ['documents', categoryId, search],
    initialPageParam: 1,
    enabled: !demo,
    queryFn: ({ pageParam }) =>
      listDocuments({
        categoryId,
        includeSubcategories: Boolean(categoryId),
        search: search.trim() || undefined,
        page: pageParam,
        pageSize,
      }),
    getNextPageParam: (last) => (last.page * last.pageSize < last.total ? last.page + 1 : undefined),
  });

  const items = documents.data?.pages.flatMap((page) => page.items) ?? [];
  const total = documents.data?.pages[0]?.total ?? 0;

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader title="بایگانی" />
        <View style={styles.filters}>
          <Input
            label="جستجو در عنوان"
            value={search}
            onChangeText={setSearch}
            autoCapitalize="none"
            autoCorrect={false}
            placeholder="عنوان سند..."
          />
          <FlatList
            horizontal
            inverted
            data={[{ id: '', name: 'همه' }, ...viewable]}
            keyExtractor={(item) => item.id || 'all'}
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.chips}
            renderItem={({ item }) => {
              const selected = (item.id || null) === categoryId;
              return (
                <Pressable
                  onPress={() => setCategoryId(item.id || null)}
                  style={[styles.chip, selected && styles.chipActive]}
                >
                  <Text style={[styles.chipText, selected && styles.chipTextActive]}>{item.name}</Text>
                </Pressable>
              );
            }}
          />
          {total > 0 ? <Text style={styles.count}>{faDigits(total)} سند</Text> : null}
        </View>

        {documents.error ? (
          <View style={styles.pad}>
            <ErrorMessage message={userMessage(documents.error)} />
          </View>
        ) : null}

        <FlatList
          data={items}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.list}
          refreshControl={
            <RefreshControl refreshing={documents.isRefetching && !documents.isFetchingNextPage} onRefresh={() => void documents.refetch()} />
          }
          onEndReached={() => {
            if (documents.hasNextPage && !documents.isFetchingNextPage) void documents.fetchNextPage();
          }}
          onEndReachedThreshold={0.4}
          ListEmptyComponent={
            documents.isLoading ? (
              <ActivityIndicator color={colors.accent} style={{ marginTop: 40 }} />
            ) : (
              <EmptyState
                title="سندی نیست"
                message="هنوز سندی در این پوشه نیست. یک سند اسکن کنید تا اینجا دیده شود."
                actionLabel="شروع اسکن"
                onAction={() => router.push('/(app)/scan')}
              />
            )
          }
          ListFooterComponent={
            documents.isFetchingNextPage ? <ActivityIndicator color={colors.accent} style={{ marginVertical: 16 }} /> : null
          }
          renderItem={({ item }) => (
            <DocumentRow
              title={item.title}
              subtitle={item.fileName}
              updatedAt={item.updatedAt}
              fileSize={item.fileSize}
              badge={item.currentVersionLabel}
              onPress={() => router.push({ pathname: '/(app)/document', params: { id: item.id } })}
            />
          )}
        />
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  filters: { paddingHorizontal: space.md, gap: space.xs },
  chips: { gap: space.sm, paddingVertical: space.xs },
  chip: {
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.line,
    backgroundColor: colors.card,
    paddingHorizontal: 14,
    paddingVertical: 8,
  },
  chipActive: { backgroundColor: colors.accentSoft, borderColor: colors.accent },
  chipText: { color: colors.ink, fontSize: 13, writingDirection: 'rtl' },
  chipTextActive: { color: colors.accent, fontWeight: '700' },
  count: {
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
    marginBottom: space.xs,
  },
  list: { padding: space.md, gap: space.sm, flexGrow: 1 },
  pad: { paddingHorizontal: space.md, paddingBottom: space.sm },
});
