import { useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Text } from '../../../src/components/AppText';
import { BrandMark } from '../../../src/components/BrandMark';
import { Button } from '../../../src/components/Button';
import { Card } from '../../../src/components/Card';
import { DottedFill } from '../../../src/components/PaperBackground';
import { PageHeader } from '../../../src/components/PageHeader';
import { colors, space } from '../../../src/theme';

export default function CaptureTabScreen() {
  const router = useRouter();

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader title="اسکن سند" />
        <View style={styles.content}>
          <BrandMark size={96} />
          <Text style={styles.title}>دوربین را آماده کنید</Text>
          <Text style={styles.copy}>برگه‌ها را پشت‌سرهم بگیرید، ترتیب را ببینید، و مستقیم در بایگانی ثبت کنید.</Text>
          <Card>
            <Text style={styles.step}>۱. عکس بگیرید یا از گالری انتخاب کنید</Text>
            <Text style={styles.step}>۲. صفحات را مرتب کنید</Text>
            <Text style={styles.step}>۳. عنوان و پوشه را بزنید و ثبت کنید</Text>
          </Card>
          <Button label="شروع اسکن" onPress={() => router.push('/(app)/scan')} />
          <Button label="باز کردن بایگانی" variant="secondary" onPress={() => router.push('/(app)/(tabs)')} />
        </View>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: {
    flex: 1,
    padding: space.lg,
    gap: space.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: {
    color: colors.accent,
    fontSize: 24,
    fontWeight: '700',
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  copy: {
    color: colors.ink,
    fontSize: 15,
    lineHeight: 24,
    textAlign: 'center',
    writingDirection: 'rtl',
    marginBottom: space.sm,
  },
  step: {
    color: colors.ink,
    fontSize: 14,
    lineHeight: 22,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
});
