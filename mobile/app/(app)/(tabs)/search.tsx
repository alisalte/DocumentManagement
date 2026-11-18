import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { searchDocuments } from '../../../src/api/search.api';
import { isDemoPreview } from '../../../src/config/demo';
import { Text } from '../../../src/components/AppText';
import { Button } from '../../../src/components/Button';
import { DocumentRow } from '../../../src/components/DocumentRow';
import { EmptyState } from '../../../src/components/EmptyState';
import { ErrorMessage } from '../../../src/components/ErrorMessage';
import { SearchField } from '../../../src/components/SearchField';
import { DottedFill } from '../../../src/components/PaperBackground';
import { PageHeader } from '../../../src/components/PageHeader';
import { colors, radius, space } from '../../../src/theme';
import { faDigits } from '../../../src/utils/format';
import { userMessage } from '../../../src/utils/errors';

export default function SearchScreen() {
  const router = useRouter();
  const [draft, setDraft] = useState('');
  const [query, setQuery] = useState('');
  const [inFile, setInFile] = useState(true);

  const result = useQuery({
    queryKey: ['search', query, inFile],
    queryFn: () => searchDocuments({ q: query, inFile, page: 1, pageSize: 30 }),
    enabled: query.trim().length > 0 && !isDemoPreview(),
  });

  function runSearch() {
    setQuery(draft.trim());
  }

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader title="جستجو" subtitle="در عنوان، مشخصات و متن داخل فایل‌ها" />
        <View style={styles.filters}>
          <SearchField
            value={draft}
            onChangeText={setDraft}
            placeholder="عنوان یا متن داخل فایل…"
            label="عبارت جستجو"
            returnKeyType="search"
            onSubmitEditing={runSearch}
          />
          <Pressable
            accessibilityRole="switch"
            accessibilityState={{ checked: inFile }}
            onPress={() => setInFile((value) => !value)}
            style={styles.toggle}
          >
            <MaterialCommunityIcons name="text-box-search-outline" size={20} color={inFile ? colors.accent : colors.muted} />
            <View style={styles.toggleBody}>
              <Text style={styles.toggleText}>جستجو در متن داخل فایل</Text>
              <Text style={styles.toggleHint}>{inFile ? 'متن اسکن‌ها هم جستجو می‌شود' : 'فقط عنوان و مشخصات'}</Text>
            </View>
            {/* The whole row toggles; the track only shows the state. */}
            <View style={[styles.track, inFile && styles.trackOn]}>
              <View style={styles.knob} />
            </View>
          </Pressable>
          <Button label="جستجو" icon="magnify" onPress={runSearch} disabled={!draft.trim()} />
          {result.data ? (
            <Text style={styles.count}>
              {faDigits(result.data.total)} نتیجه
              {result.data.degraded ? ' · موتور جستجو در دسترس نیست؛ فقط عنوان' : ''}
            </Text>
          ) : null}
          {result.error ? <ErrorMessage message={userMessage(result.error)} /> : null}
        </View>

        <FlatList
          data={result.data?.hits ?? []}
          keyExtractor={(item) => `${item.documentId}:${item.versionId}`}
          contentContainerStyle={styles.list}
          ListEmptyComponent={
            !query ? (
              <EmptyState icon="magnify" title="چیزی جستجو کنید" message="عنوان سند یا بخشی از متن داخل فایل را بنویسید." />
            ) : result.isLoading ? (
              <ActivityIndicator color={colors.accent} style={{ marginTop: 40 }} />
            ) : (
              <EmptyState icon="file-search-outline" title="نتیجه‌ای نبود" message="عبارت دیگری را امتحان کنید یا فقط عنوان را جستجو کنید." />
            )
          }
          renderItem={({ item }) => (
            <DocumentRow
              title={item.title}
              subtitle={item.fileName}
              updatedAt={item.updatedAt}
              badge={item.label}
              mimeType={item.mimeType}
              onPress={() => router.push({ pathname: '/(app)/document', params: { id: item.documentId } })}
            />
          )}
        />
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  filters: { paddingHorizontal: space.md, gap: space.sm },
  toggle: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.line,
    backgroundColor: colors.card,
    paddingVertical: 10,
    paddingHorizontal: 14,
  },
  toggleBody: { flex: 1, gap: 1 },
  track: {
    width: 40,
    height: 24,
    borderRadius: 12,
    padding: 3,
    backgroundColor: colors.lineStrong,
    // Right to left: off rests on the right (start), on slides to the left (end).
    alignItems: 'flex-start',
  },
  trackOn: { backgroundColor: colors.accent, alignItems: 'flex-end' },
  knob: { width: 18, height: 18, borderRadius: 9, backgroundColor: '#ffffff' },
  toggleText: { color: colors.ink, textAlign: 'right', writingDirection: 'rtl', fontSize: 14, fontWeight: '600' },
  toggleHint: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl', fontSize: 12 },
  count: {
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  list: { padding: space.md, gap: 10, flexGrow: 1 },
});
