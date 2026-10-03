import { useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../../src/auth/AuthProvider';
import { Text } from '../../src/components/AppText';
import { BrandMark } from '../../src/components/BrandMark';
import { Button } from '../../src/components/Button';
import { PaperBackground } from '../../src/components/PaperBackground';
import { colors } from '../../src/theme';

export default function HomeScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);
  const name = user?.displayName || user?.username || '';

  return (
    <PaperBackground>
      <BrandMark size={112} />
      <Text style={styles.header}>{name ? `سلام، ${name}` : 'اسکنر اسناد'}</Text>
      <Text style={styles.paragraph}>برگه‌ها را اسکن کنید و همان‌جا در بایگانی ثبت کنید.</Text>
      <View style={styles.actions}>
        <Button label="اسکن سند" onPress={() => router.push('/(app)/scan')} />
        <Button
          label={leaving ? 'در حال خروج...' : 'خروج'}
          variant="secondary"
          loading={leaving}
          onPress={() => {
            setLeaving(true);
            void logout().finally(() => setLeaving(false));
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
  paragraph: {
    color: colors.ink,
    fontSize: 16,
    lineHeight: 26,
    textAlign: 'center',
    writingDirection: 'rtl',
    marginBottom: 8,
  },
  actions: { width: '100%', gap: 4, marginTop: 8 },
});
