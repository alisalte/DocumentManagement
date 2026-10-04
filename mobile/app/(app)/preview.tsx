import { Redirect, useLocalSearchParams, useRouter } from 'expo-router';
import { ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '../../src/components/Button';
import { DottedFill } from '../../src/components/PaperBackground';
import { PageHeader } from '../../src/components/PageHeader';
import { ScanPreview } from '../../src/features/scanner/components/ScanPreview';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors, space } from '../../src/theme';
import { faDigits } from '../../src/utils/format';

export default function PreviewScreen() {
  const router = useRouter();
  const session = useScanSession();
  const params = useLocalSearchParams<{ format?: string }>();

  if (session.pages.length === 0) return <Redirect href="/(app)/scan" />;

  const singlePage = session.pages.length === 1;

  function goUpload(format: 'pdf' | 'image') {
    router.push({ pathname: '/(app)/upload', params: { format } });
  }

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe}>
        <PageHeader title="مرور صفحات" onBack={() => router.back()} />
        <ScrollView contentContainerStyle={styles.content}>
          <Text style={styles.hint}>{faDigits(session.pages.length)} صفحه. ترتیب ثبت از صفحهٔ اول است.</Text>
          <ScanPreview pages={session.pages} onMove={session.move} onDelete={session.remove} />
          <View style={styles.actions}>
            <Button label="افزودن صفحه" variant="secondary" onPress={() => router.push('/(app)/scan')} />
            <Button label="ثبت به‌صورت PDF" onPress={() => goUpload('pdf')} />
            <Button
              label="ثبت به‌صورت عکس"
              variant="secondary"
              onPress={() => goUpload('image')}
              disabled={!singlePage}
            />
            {!singlePage ? (
              <Text style={styles.hint}>برای ثبت عکس، فقط یک صفحه بگذارید. چند صفحه را به‌صورت PDF ثبت کنید.</Text>
            ) : null}
            {params.format === 'image' && !singlePage ? (
              <Text style={styles.hint}>حالت عکس فقط با یک صفحه در دسترس است.</Text>
            ) : null}
          </View>
        </ScrollView>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { padding: space.md, gap: space.md, paddingBottom: space.xl },
  hint: { color: colors.muted, fontSize: 15, textAlign: 'right', writingDirection: 'rtl' },
  actions: { gap: space.sm, marginTop: space.sm },
});
