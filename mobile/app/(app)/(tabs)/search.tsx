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
import { Input } from '../../../src/components/Input';
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
        <PageHeader title="جستجو" />
        <View style={styles.filters}>
          <Input
            label="عبارت جستجو"
            value={draft}
            onChangeText={setDraft}
            autoCapitalize="none"
            autoCorrect={false}
            placeholder="عنوان یا متن داخل فایل..."
            onBlur={() => undefined}
          />
          <Pressable onPress={() => setInFile((value) => !value)} style={[styles.toggle, inFile && styles.toggleOn]}>
            <Text style={[styles.toggleText, inFile && styles.toggleTextOn]}>
              {inFile ? 'جستجو داخل متن فایل هم فعال است' : 'فقط عنوان و مشخصات'}
            </Text>
          </Pressable>
          <Button label="جستجو" onPress={runSearch} disabled={!draft.trim()} />
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
              <EmptyState title="چیزی جستجو کنید" message="عنوان سند یا بخشی از متن داخل فایل را بنویسید." />
            ) : result.isLoading ? (
              <ActivityIndicator color={colors.accent} style={{ marginTop: 40 }} />
            ) : (
              <EmptyState title="نتیجه‌ای نبود" message="عبارت دیگری را امتحان کنید یا فقط عنوان را جستجو کنید." />
            )
          }
          renderItem={({ item }) => (
            <DocumentRow
              title={item.title}
              subtitle={item.fileName}
              updatedAt={item.updatedAt}
              badge={item.label}
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
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.line,
    backgroundColor: colors.card,
    padding: space.md,
  },
  toggleOn: { borderColor: colors.accent, backgroundColor: colors.accentSoft },
  toggleText: { color: colors.ink, textAlign: 'right', writingDirection: 'rtl', fontSize: 14 },
  toggleTextOn: { color: colors.accent, fontWeight: '700' },
  count: {
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  list: { padding: space.md, gap: space.sm, flexGrow: 1 },
});
