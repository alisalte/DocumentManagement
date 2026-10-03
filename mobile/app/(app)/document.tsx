import { useQuery } from '@tanstack/react-query';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { SafeAreaView } from 'react-native-safe-area-context';
import { getDocument } from '../../src/api/documents.api';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { Loading } from '../../src/components/Loading';
import { PageHeader } from '../../src/components/PageHeader';
import { colors, radius, space } from '../../src/theme';
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
  const archived = document.data?.status === 'Archived';

  return (
    <SafeAreaView style={styles.safe}>
      <PageHeader title="سند" onBack={() => router.back()} />
      <View style={styles.content}>
        {!id ? <ErrorMessage message="شناسهٔ سند موجود نیست." /> : null}
        {document.isLoading ? <Loading label="در حال دریافت سند..." /> : null}
        {document.error ? <ErrorMessage message={userMessage(document.error)} /> : null}
        {document.data ? (
          <Card>
            <View style={styles.titleRow}>
              <Text style={styles.title}>{document.data.title}</Text>
              <View style={[styles.pill, archived ? styles.pillWarn : styles.pillOk]}>
                <Text style={[styles.pillText, archived ? styles.pillTextWarn : styles.pillTextOk]}>
                  {statusLabels[document.data.status] ?? document.data.status}
                </Text>
              </View>
            </View>
            <Text style={styles.line}>{document.data.categoryName}</Text>
            {document.data.description ? <Text style={styles.line}>{document.data.description}</Text> : null}
            <Text style={styles.muted}>{document.data.id}</Text>
          </Card>
        ) : null}
        {document.isError ? <Button label="تلاش دوباره" onPress={() => void document.refetch()} /> : null}
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.md, gap: space.md },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  title: { flex: 1, fontSize: 22, fontWeight: '800', color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  line: { color: colors.ink, textAlign: 'right', writingDirection: 'rtl', fontSize: 15 },
  muted: { color: colors.muted, textAlign: 'right', fontSize: 12 },
  pill: { borderRadius: radius.pill, paddingHorizontal: 12, paddingVertical: 6 },
  pillOk: { backgroundColor: colors.accentSoft },
  pillWarn: { backgroundColor: colors.warningSoft },
  pillText: { fontSize: 13, fontWeight: '700', writingDirection: 'rtl' },
  pillTextOk: { color: colors.accent },
  pillTextWarn: { color: colors.warning },
});
