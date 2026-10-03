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
import { DottedFill } from '../../src/components/PaperBackground';
import { PageHeader } from '../../src/components/PageHeader';
import { colors, radius, space } from '../../src/theme';
import { formatBytes, formatDateFa } from '../../src/utils/format';
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
  const data = document.data;
  const archived = data?.status === 'Archived';

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe}>
        <PageHeader title="جزئیات سند" onBack={() => router.back()} />
        <View style={styles.content}>
          {!id ? <ErrorMessage message="شناسهٔ سند موجود نیست." /> : null}
          {document.isLoading ? <Loading label="در حال دریافت سند..." /> : null}
          {document.error ? <ErrorMessage message={userMessage(document.error)} /> : null}
          {data ? (
            <Card>
              <View style={styles.titleRow}>
                <Text style={styles.title}>{data.title}</Text>
                <View style={[styles.pill, archived ? styles.pillWarn : styles.pillOk]}>
                  <Text style={[styles.pillText, archived ? styles.pillTextWarn : styles.pillTextOk]}>
                    {statusLabels[data.status] ?? data.status}
                  </Text>
                </View>
              </View>
              <Text style={styles.line}>{data.categoryName}</Text>
              {data.description ? <Text style={styles.line}>{data.description}</Text> : null}
              {data.tags && data.tags.length > 0 ? (
                <Text style={styles.line}>برچسب‌ها: {data.tags.map((tag) => tag.name).join('، ')}</Text>
              ) : null}
              <Text style={styles.muted}>به‌روزرسانی: {formatDateFa(data.updatedAt)}</Text>
              {data.fileSize != null ? <Text style={styles.muted}>حجم: {formatBytes(data.fileSize)}</Text> : null}
              {data.latestVersionNumber != null ? (
                <Text style={styles.muted}>آخرین نسخه: {data.latestVersionNumber}</Text>
              ) : null}
              <Text style={styles.muted}>{data.id}</Text>
            </Card>
          ) : null}
          {document.isError ? <Button label="تلاش دوباره" onPress={() => void document.refetch()} /> : null}
          <Button label="اسکن سند جدید" variant="secondary" onPress={() => router.push('/(app)/scan')} />
          <Button label="بازگشت به بایگانی" variant="ghost" onPress={() => router.replace('/(app)/(tabs)')} />
        </View>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { padding: space.md, gap: space.md },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  title: { flex: 1, fontSize: 22, fontWeight: '700', color: colors.accent, textAlign: 'right', writingDirection: 'rtl' },
  line: { color: colors.ink, textAlign: 'right', writingDirection: 'rtl', fontSize: 15 },
  muted: { color: colors.muted, textAlign: 'right', fontSize: 12, writingDirection: 'rtl' },
  pill: { borderRadius: radius.pill, paddingHorizontal: 12, paddingVertical: 6 },
  pillOk: { backgroundColor: colors.accentSoft },
  pillWarn: { backgroundColor: colors.warningSoft },
  pillText: { fontSize: 13, fontWeight: '700', writingDirection: 'rtl' },
  pillTextOk: { color: colors.accent },
  pillTextWarn: { color: colors.warning },
});
