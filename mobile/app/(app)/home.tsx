import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '../../src/components/AppText';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../src/auth/AuthProvider';
import { Button } from '../../src/components/Button';
import { colors, radius, space } from '../../src/theme';

type StepIcon = 'scan' | 'pages' | 'file';
type Tone = 'ok' | 'muted';

export default function HomeScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);
  const name = user?.displayName || user?.username || '';
  const letter = name.trim().charAt(0) || '؟';

  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll} bounces={false}>
        <View style={styles.intro}>
          <View style={styles.avatar}>
            <Text style={styles.avatarText}>{letter}</Text>
            <View style={styles.dot} />
          </View>
          <Text style={styles.greeting}>سلام، {name}</Text>
          <Text style={styles.subtitle}>برگه‌ها را اسکن کنید و همان‌جا در بایگانی ثبت کنید.</Text>
        </View>
        <View style={styles.sheet}>
          <Step title="اسکن" detail="چند صفحه با دوربین یا گالری" badge="آماده" tone="ok" icon="scan" />
          <View style={styles.divider} />
          <Step title="مرور" detail="حذف و جابه‌جایی صفحه‌ها" badge="مرحله ۲" tone="muted" icon="pages" />
          <View style={styles.divider} />
          <Step title="ثبت" detail="عنوان، پوشه و نوع سند" badge="مرحله ۳" tone="muted" icon="file" />
          <Button label="اسکن سند" onPress={() => router.push('/(app)/scan')} style={styles.cta} />
          <Pressable
            accessibilityRole="button"
            disabled={leaving}
            onPress={() => {
              setLeaving(true);
              void logout().finally(() => setLeaving(false));
            }}
            style={styles.logout}
          >
            <Text style={styles.logoutText}>{leaving ? 'در حال خروج...' : 'خروج از حساب'}</Text>
          </Pressable>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

function Step({
  title,
  detail,
  badge,
  tone,
  icon,
}: {
  title: string;
  detail: string;
  badge: string;
  tone: Tone;
  icon: StepIcon;
}) {
  return (
    <View style={styles.step}>
      <Glyph kind={icon} />
      <View style={styles.stepText}>
        <Text style={styles.stepTitle}>{title}</Text>
        <Text style={styles.stepDetail}>{detail}</Text>
      </View>
      <View style={[styles.pill, tone === 'ok' ? styles.pillOk : styles.pillMuted]}>
        <Text style={[styles.pillText, tone === 'ok' ? styles.pillTextOk : styles.pillTextMuted]}>{badge}</Text>
      </View>
    </View>
  );
}

function Glyph({ kind }: { kind: StepIcon }) {
  return (
    <View style={styles.glyph}>
      {kind === 'scan' ? <View style={styles.lens} /> : null}
      {kind === 'pages' ? (
        <View style={styles.bars}>
          <View style={styles.bar} />
          <View style={[styles.bar, styles.barShort]} />
          <View style={styles.bar} />
        </View>
      ) : null}
      {kind === 'file' ? <View style={styles.file} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  scroll: { flexGrow: 1 },
  intro: { paddingHorizontal: space.lg, paddingTop: space.md, paddingBottom: space.xl, gap: space.sm },
  avatar: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: colors.accentSoft,
    alignItems: 'center',
    justifyContent: 'center',
    alignSelf: 'flex-start',
  },
  avatarText: { color: colors.accent, fontSize: 22, fontWeight: '800' },
  dot: {
    position: 'absolute',
    width: 12,
    height: 12,
    borderRadius: 6,
    backgroundColor: colors.warning,
    borderWidth: 2,
    borderColor: colors.background,
    bottom: 2,
    start: 2,
  },
  greeting: { fontSize: 30, fontWeight: '800', color: colors.ink, textAlign: 'right', writingDirection: 'rtl', marginTop: space.sm },
  subtitle: { color: colors.ink, fontSize: 16, lineHeight: 26, textAlign: 'right', writingDirection: 'rtl' },
  sheet: {
    flexGrow: 1,
    backgroundColor: colors.card,
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
    paddingHorizontal: space.lg,
    paddingTop: space.lg,
    paddingBottom: space.xl,
  },
  step: { flexDirection: 'row', alignItems: 'center', gap: space.md, paddingVertical: space.md },
  glyph: {
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: colors.accentSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  lens: { width: 16, height: 16, borderRadius: 8, borderWidth: 3, borderColor: colors.accent },
  bars: { gap: 4, width: 18 },
  bar: { height: 3, borderRadius: 2, backgroundColor: colors.accent },
  barShort: { width: 12 },
  file: { width: 16, height: 20, borderRadius: 3, borderWidth: 2, borderColor: colors.accent },
  stepText: { flex: 1, gap: 2 },
  stepTitle: { color: colors.ink, fontSize: 18, fontWeight: '800', textAlign: 'right', writingDirection: 'rtl' },
  stepDetail: { color: colors.muted, fontSize: 14, textAlign: 'right', writingDirection: 'rtl' },
  pill: { borderRadius: radius.pill, paddingHorizontal: 12, paddingVertical: 6 },
  pillOk: { backgroundColor: colors.accentSoft },
  pillMuted: { backgroundColor: colors.background },
  pillText: { fontSize: 13, fontWeight: '700', writingDirection: 'rtl' },
  pillTextOk: { color: colors.accent },
  pillTextMuted: { color: colors.muted },
  divider: { height: 1, backgroundColor: colors.line },
  cta: { marginTop: space.lg },
  logout: { alignItems: 'center', paddingVertical: space.md },
  logoutText: { color: colors.accent, fontSize: 16, fontWeight: '700', writingDirection: 'rtl' },
});
