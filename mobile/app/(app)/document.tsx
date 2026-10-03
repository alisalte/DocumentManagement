import { useQuery } from '@tanstack/react-query';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { getDocument } from '../../src/api/documents.api';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { Loading } from '../../src/components/Loading';
import { PageHeader } from '../../src/components/PageHeader';
import { colors, space } from '../../src/theme';
import { userMessage } from '../../src/utils/errors';

const statusLabels: Record<string, string> = {
  Active: 'فعال',
  Archived: 'بایگانی‌شده',
};

function one(value: string | string[] | undefined): string {
  return Array.isArray(value) ? (value[0] ?? '') : (value ?? '');
}

export default function DocumentScreen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const id = one(params.id);
  const router = useRouter();
  const document = useQuery({
    queryKey: ['document', id],
    queryFn: () => getDocument(id),
    enabled: id.length > 0,
  });

  return (
    <SafeAreaView style={styles.safe}>
      <PageHeader title="سند" onBack={() => router.back()} />
      <View style={styles.content}>
        {!id ? <ErrorMessage message="شناسهٔ سند موجود نیست." /> : null}
        {document.isLoading ? <Loading label="در حال دریافت سند..." /> : null}
        {document.error ? <ErrorMessage message={userMessage(document.error)} /> : null}
        {document.data ? (
          <View style={styles.card}>
            <Text style={styles.title}>{document.data.title}</Text>
            <Text style={styles.line}>پوشه: {document.data.categoryName}</Text>
            <Text style={styles.line}>وضعیت: {statusLabels[document.data.status] ?? document.data.status}</Text>
            {document.data.description ? <Text style={styles.line}>{document.data.description}</Text> : null}
            <Text style={styles.muted}>{document.data.id}</Text>
          </View>
        ) : null}
        {document.isError ? <Button label="تلاش دوباره" onPress={() => void document.refetch()} /> : null}
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.md, gap: space.md },
  card: { backgroundColor: colors.card, borderRadius: 12, padding: space.md, gap: space.sm, borderWidth: 1, borderColor: colors.line },
  title: { fontSize: 20, fontWeight: '700', color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  line: { color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  muted: { color: colors.muted, textAlign: 'right', fontSize: 12 },
});
