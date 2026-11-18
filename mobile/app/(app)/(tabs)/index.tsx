import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Platform, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { listCategories, listDocuments } from '../../../src/api/documents.api';
import { demoCategories, demoDocuments, isDemoPreview } from '../../../src/config/demo';
import { Text } from '../../../src/components/AppText';
import { DocumentRow } from '../../../src/components/DocumentRow';
import { EmptyState } from '../../../src/components/EmptyState';
import { ErrorMessage } from '../../../src/components/ErrorMessage';
import { SearchField } from '../../../src/components/SearchField';
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
    () => (demo ? demoCategories : (categories.data ?? [])).filter((category) => category.isActive && category.canView !== false),
    [categories.data, demo],
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

  const items = demo
    ? demoDocuments.filter((doc) => (!categoryId || doc.categoryId === categoryId) && doc.title.includes(search.trim()))
    : (documents.data?.pages.flatMap((page) => page.items) ?? []);
  const total = demo ? items.length : (documents.data?.pages[0]?.total ?? 0);

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader
          title="بایگانی"
          subtitle={total > 0 ? `${faDigits(total)} سند در دسترس شما` : 'اسناد پوشه‌هایی که به آن‌ها دسترسی دارید'}
          actionLabel="اسکن"
          onAction={() => router.push('/(app)/scan')}
        />
        <View style={styles.filters}>
          <SearchField value={search} onChangeText={setSearch} placeholder="جستجو در عنوان اسناد…" />
          <FlatList
            horizontal
            // A phone starts the row on the right through the flip; the web page is already right to left.
            inverted={Platform.OS !== 'web'}
            data={[{ id: '', name: 'همه' }, ...viewable]}
            keyExtractor={(item) => item.id || 'all'}
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.chips}
            renderItem={({ item }) => {
              const selected = (item.id || null) === categoryId;
              return (
                <Pressable
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  onPress={() => setCategoryId(item.id || null)}
                  style={[styles.chip, selected && styles.chipActive]}
                >
                  <MaterialCommunityIcons
                    name={item.id ? 'folder-outline' : 'view-grid-outline'}
                    size={16}
                    color={selected ? '#ffffff' : colors.muted}
                  />
                  <Text style={[styles.chipText, selected && styles.chipTextActive]}>{item.name}</Text>
                </Pressable>
              );
            }}
          />
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
            ) : search.trim() ? (
              <EmptyState
                icon="file-search-outline"
                title="نتیجه‌ای نبود"
                message="عبارت دیگری را امتحان کنید یا جستجو را پاک کنید."
              />
            ) : (
              <EmptyState
                icon="folder-open-outline"
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
              mimeType={item.mimeType}
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
  filters: { paddingHorizontal: space.md, gap: space.sm, paddingTop: space.xs },
  chips: { gap: 8, paddingVertical: 2 },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.lineStrong,
    backgroundColor: colors.card,
    paddingHorizontal: 14,
    height: 36,
  },
  chipActive: { backgroundColor: colors.accent, borderColor: colors.accent },
  chipText: { color: colors.text, fontSize: 13, fontWeight: '500', writingDirection: 'rtl' },
  chipTextActive: { color: '#ffffff', fontWeight: '700' },
  list: { padding: space.md, paddingTop: space.sm, gap: 10, flexGrow: 1 },
  pad: { paddingHorizontal: space.md, paddingBottom: space.sm },
});
