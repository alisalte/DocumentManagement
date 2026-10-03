import { useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import * as Sharing from 'expo-sharing';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors, space } from '../../src/theme';

function one(value: string | string[] | undefined): string {
  return Array.isArray(value) ? (value[0] ?? '') : (value ?? '');
}

export default function DocumentSuccessScreen() {
  const params = useLocalSearchParams<{ documentId?: string; versionId?: string; label?: string; title?: string }>();
  const documentId = one(params.documentId);
  const label = one(params.label);
  const title = one(params.title);
  const router = useRouter();
  const session = useScanSession();
  const [shareError, setShareError] = useState<string | null>(null);

  async function sharePdf() {
    if (!session.pdfUri) return;
    setShareError(null);
    try {
      const available = await Sharing.isAvailableAsync();
      if (!available) {
        setShareError('اشتراک‌گذاری روی این دستگاه در دسترس نیست.');
        return;
      }
      await Sharing.shareAsync(session.pdfUri, { mimeType: 'application/pdf', UTI: 'com.adobe.pdf', dialogTitle: 'سند اسکن‌شده' });
    } catch {
      setShareError('اشتراک‌گذاری PDF ناموفق بود.');
    }
  }

  return (
    <SafeAreaView style={styles.safe}>
      <View style={styles.content}>
        <Text style={styles.mark}>✓</Text>
        <Text style={styles.title}>سند با موفقیت ثبت شد</Text>
        {title ? <Text style={styles.line}>{title}</Text> : null}
        {label ? <Text style={styles.line}>نسخه {label}</Text> : null}
        {documentId ? <Text style={styles.muted}>{documentId}</Text> : null}
        <ErrorMessage message={shareError} />
        <View style={styles.actions}>
          {documentId ? (
            <Button label="مشاهده سند" onPress={() => router.push({ pathname: '/(app)/document', params: { id: documentId } })} />
          ) : null}
          {session.pdfUri ? <Button label="اشتراک‌گذاری PDF" variant="secondary" onPress={() => void sharePdf()} /> : null}
          <Button
            label="ثبت سند جدید"
            variant="secondary"
            onPress={() => {
              session.reset();
              router.replace('/(app)/scan');
            }}
          />
          <Button
            label="بازگشت به خانه"
            variant="ghost"
            onPress={() => {
              session.reset();
              router.replace('/(app)/home');
            }}
          />
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1, justifyContent: 'center', padding: space.lg, gap: space.sm },
  mark: { fontSize: 42, color: colors.success, textAlign: 'center' },
  title: { fontSize: 24, fontWeight: '700', color: colors.ink, textAlign: 'center', writingDirection: 'rtl' },
  line: { fontSize: 16, color: colors.ink, textAlign: 'center', writingDirection: 'rtl' },
  muted: { color: colors.muted, textAlign: 'center', fontSize: 12 },
  actions: { gap: space.sm, marginTop: space.lg },
});
