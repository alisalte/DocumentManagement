import { useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuth } from '../../src/auth/AuthProvider';
import { Button } from '../../src/components/Button';
import { colors, space } from '../../src/theme';

export default function HomeScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);

  return (
    <SafeAreaView style={styles.safe}>
      <View style={styles.content}>
        <Text style={styles.kicker}>بایگانی اسناد</Text>
        <Text style={styles.title}>اسکنر اسناد</Text>
        <Text style={styles.user}>{user?.displayName || user?.username}</Text>
        {user?.username ? <Text style={styles.username}>{user.username}</Text> : null}
        <Button label="اسکن سند" onPress={() => router.push('/(app)/scan')} />
        <Button
          label="خروج"
          variant="ghost"
          loading={leaving}
          onPress={() => {
            setLeaving(true);
            void logout().finally(() => setLeaving(false));
          }}
        />
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1, justifyContent: 'center', padding: space.lg, gap: space.md },
  kicker: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl' },
  title: { fontSize: 32, fontWeight: '700', color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  user: { fontSize: 18, color: colors.ink, textAlign: 'right', writingDirection: 'rtl' },
  username: { color: colors.muted, textAlign: 'right', writingDirection: 'rtl', marginBottom: space.lg },
});
