import { useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { Button } from '../../src/components/Button';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { PaperBackground } from '../../src/components/PaperBackground';
import { savePreparedFile } from '../../src/features/scanner/save-file';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors } from '../../src/theme';

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
  const [saving, setSaving] = useState(false);

  async function saveFile() {
    if (!session.prepared) return;
    setShareError(null);
    setSaving(true);
    try {
      await savePreparedFile(session.prepared);
    } catch (caught) {
      const message =
        caught instanceof Error && /[\u0600-\u06FF]/.test(caught.message)
          ? caught.message
          : 'ذخیره یا اشتراک‌گذاری فایل ناموفق بود.';
      setShareError(message);
    } finally {
      setSaving(false);
    }
  }

  const saveLabel = session.prepared?.format === 'image' ? 'ذخیره عکس روی گوشی' : 'ذخیره PDF روی گوشی';

  return (
    <PaperBackground>
      <Text style={styles.header}>سند ثبت شد</Text>
      <Text style={styles.paragraph}>در بایگانی ذخیره شد.</Text>
      {title ? <Text style={styles.line}>{title}</Text> : null}
      {label ? <Text style={styles.muted}>نسخه {label}</Text> : null}
      <View style={styles.full}>
        <ErrorMessage message={shareError} />
      </View>
      <View style={styles.actions}>
        {documentId ? (
          <Button label="مشاهده سند" onPress={() => router.push({ pathname: '/(app)/document', params: { id: documentId } })} />
        ) : null}
        {session.prepared ? (
          <Button label={saveLabel} variant="secondary" loading={saving} onPress={() => void saveFile()} />
        ) : null}
        <Button
          label="ثبت سند جدید"
          variant="secondary"
          onPress={() => {
            session.reset();
            router.replace('/(app)/scan');
          }}
        />
        <Button
          label="بازگشت به بایگانی"
          variant="ghost"
          onPress={() => {
            session.reset();
            router.replace('/(app)/(tabs)');
          }}
        />
      </View>
    </PaperBackground>
  );
}

const styles = StyleSheet.create({
  header: {
    fontSize: 26,
    fontWeight: '700',
    color: colors.accent,
    textAlign: 'center',
    writingDirection: 'rtl',
    paddingVertical: 12,
  },
  paragraph: { color: colors.ink, fontSize: 16, lineHeight: 26, textAlign: 'center', writingDirection: 'rtl' },
  line: { fontSize: 16, fontWeight: '700', color: colors.ink, textAlign: 'center', writingDirection: 'rtl' },
  muted: { color: colors.muted, textAlign: 'center', writingDirection: 'rtl' },
  full: { width: '100%' },
  actions: { width: '100%', gap: 4, marginTop: 12 },
});
