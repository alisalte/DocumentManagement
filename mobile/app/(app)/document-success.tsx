import { useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import * as Sharing from 'expo-sharing';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors, radius, shadow, space } from '../../src/theme';

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
        <View style={styles.card}>
          <View style={styles.banner}>
            <Text style={styles.bannerTitle}>سند ثبت شد</Text>
            <Text style={styles.bannerHint}>در بایگانی ذخیره شد</Text>
          </View>
          <View style={styles.body}>
            <View style={styles.ring}>
              <Text style={styles.check}>✓</Text>
            </View>
            {title ? <Text style={styles.line}>{title}</Text> : null}
            {label ? <Text style={styles.muted}>نسخه {label}</Text> : null}
            <ErrorMessage message={shareError} />
            <View style={styles.actions}>
              {documentId ? (
                <Button label="مشاهده سند" onPress={() => router.push({ pathname: '/(app)/document', params: { id: documentId } })} />
              ) : null}
              {session.pdfUri ? <Button label="اشتراک PDF" variant="secondary" onPress={() => void sharePdf()} /> : null}
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
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1, justifyContent: 'center', padding: space.lg },
  card: { backgroundColor: colors.card, borderRadius: radius.lg, overflow: 'hidden', ...shadow.card },
  banner: { backgroundColor: colors.accent, paddingHorizontal: space.lg, paddingVertical: space.lg, gap: space.xs },
  bannerTitle: { color: '#fff', fontSize: 26, fontWeight: '800', textAlign: 'center', writingDirection: 'rtl' },
  bannerHint: { color: '#E6E0FF', fontSize: 15, textAlign: 'center', writingDirection: 'rtl' },
  body: { padding: space.lg, gap: space.sm, alignItems: 'center' },
  ring: {
    width: 112,
    height: 112,
    borderRadius: 56,
    borderWidth: 8,
    borderColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
    marginVertical: space.sm,
  },
  check: { color: colors.accent, fontSize: 42, fontWeight: '700' },
  line: { fontSize: 18, fontWeight: '700', color: colors.ink, textAlign: 'center', writingDirection: 'rtl' },
  muted: { color: colors.muted, textAlign: 'center', writingDirection: 'rtl' },
  actions: { alignSelf: 'stretch', gap: space.sm, marginTop: space.md },
});
