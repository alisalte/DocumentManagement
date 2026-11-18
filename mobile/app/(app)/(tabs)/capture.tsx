import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import type { ComponentProps } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Text } from '../../../src/components/AppText';
import { BrandMark } from '../../../src/components/BrandMark';
import { Button } from '../../../src/components/Button';
import { DottedFill } from '../../../src/components/PaperBackground';
import { PageHeader } from '../../../src/components/PageHeader';
import { colors, radius, space } from '../../../src/theme';

const steps: Array<{ icon: ComponentProps<typeof MaterialCommunityIcons>['name']; title: string; detail: string }> = [
  { icon: 'camera-outline', title: 'عکس بگیرید', detail: 'برگه‌ها را پشت‌سرهم بگیرید یا از گالری انتخاب کنید.' },
  { icon: 'swap-vertical', title: 'مرتب کنید', detail: 'ترتیب صفحه‌ها را ببینید و جابه‌جا کنید.' },
  { icon: 'folder-upload-outline', title: 'ثبت کنید', detail: 'عنوان و پوشه را بزنید؛ سند در بایگانی ثبت می‌شود.' },
];

export default function CaptureTabScreen() {
  const router = useRouter();

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader title="اسکن سند" />
        <ScrollView contentContainerStyle={styles.content}>
          <View style={styles.hero}>
            <BrandMark size={80} />
            <Text style={styles.title}>دوربین را آماده کنید</Text>
            <Text style={styles.copy}>در سه قدم یک سند کاغذی را به بایگانی اضافه کنید.</Text>
          </View>

          <View style={styles.steps}>
            {steps.map((step, index) => (
              <View key={step.title} style={[styles.step, index > 0 && styles.stepBorder]}>
                <View style={styles.stepIcon}>
                  <MaterialCommunityIcons name={step.icon} size={20} color={colors.accent} />
                </View>
                <View style={styles.stepBody}>
                  <Text style={styles.stepTitle}>{step.title}</Text>
                  <Text style={styles.stepDetail}>{step.detail}</Text>
                </View>
              </View>
            ))}
          </View>

          <Button label="شروع اسکن" icon="camera-outline" onPress={() => router.push('/(app)/scan')} />
          <Button label="باز کردن بایگانی" variant="secondary" onPress={() => router.push('/(app)/(tabs)')} />
        </ScrollView>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { flexGrow: 1, padding: space.md, gap: space.md, justifyContent: 'center' },
  hero: { alignItems: 'center', gap: 6, marginBottom: space.sm },
  title: {
    marginTop: space.md,
    color: colors.ink,
    fontSize: 22,
    fontWeight: '800',
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  copy: { color: colors.muted, fontSize: 14, lineHeight: 23, textAlign: 'center', writingDirection: 'rtl' },
  steps: {
    backgroundColor: colors.card,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.line,
    overflow: 'hidden',
  },
  step: { flexDirection: 'row', alignItems: 'center', gap: 12, padding: 14 },
  stepBorder: { borderTopWidth: 1, borderTopColor: colors.line },
  stepIcon: {
    width: 38,
    height: 38,
    borderRadius: radius.md,
    backgroundColor: colors.accentSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  stepBody: { flex: 1, gap: 2 },
  stepTitle: { color: colors.ink, fontSize: 15, fontWeight: '700', textAlign: 'right', writingDirection: 'rtl' },
  stepDetail: { color: colors.muted, fontSize: 13, lineHeight: 21, textAlign: 'right', writingDirection: 'rtl' },
});
