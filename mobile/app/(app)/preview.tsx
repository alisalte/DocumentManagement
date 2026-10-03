import { Redirect, useRouter } from 'expo-router';
import { ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button } from '../../src/components/Button';
import { PageHeader } from '../../src/components/PageHeader';
import { ScanPreview } from '../../src/features/scanner/components/ScanPreview';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors, space } from '../../src/theme';
import { faDigits } from '../../src/utils/format';

export default function PreviewScreen() {
  const router = useRouter();
  const session = useScanSession();

  if (session.pages.length === 0) return <Redirect href="/(app)/scan" />;

  return (
    <SafeAreaView style={styles.safe}>
      <PageHeader title="مرور صفحات" onBack={() => router.back()} />
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.hint}>{faDigits(session.pages.length)} صفحه. ترتیب ثبت از صفحهٔ اول است.</Text>
        <ScanPreview pages={session.pages} onMove={session.move} onDelete={session.remove} />
        <View style={styles.actions}>
          <Button label="افزودن صفحه" variant="secondary" onPress={() => router.push('/(app)/scan')} />
          <Button label="ساخت PDF" onPress={() => router.push('/(app)/upload')} />
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: space.md, gap: space.md, paddingBottom: space.xl },
  hint: { color: colors.muted, fontSize: 15, textAlign: 'right', writingDirection: 'rtl' },
  actions: { gap: space.sm, marginTop: space.sm },
});
