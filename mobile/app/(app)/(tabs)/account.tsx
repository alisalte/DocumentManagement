import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { listTasks } from '../../../src/api/workflow.api';
import { isDemoPreview } from '../../../src/config/demo';
import { useAuth } from '../../../src/auth/AuthProvider';
import { Text } from '../../../src/components/AppText';
import { Button } from '../../../src/components/Button';
import { Card } from '../../../src/components/Card';
import { DottedFill } from '../../../src/components/PaperBackground';
import { PageHeader } from '../../../src/components/PageHeader';
import { currentApiUrl } from '../../../src/config/server-url-store';
import { colors, space } from '../../../src/theme';
import { faDigits } from '../../../src/utils/format';

export default function AccountScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);
  const tasks = useQuery({ queryKey: ['workflow-tasks'], queryFn: listTasks, enabled: !isDemoPreview() });
  const pending = (tasks.data ?? []).filter((task) => task.status === 'Pending' && task.canAct).length;
  const server = currentApiUrl();

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader title="حساب کاربری" />
        <View style={styles.content}>
          <Card>
            <Text style={styles.name}>{user?.displayName || user?.username || 'کاربر'}</Text>
            <Text style={styles.line}>{user?.username}</Text>
            {user?.isSystemAdmin ? <Text style={styles.badge}>مدیر سیستم</Text> : null}
          </Card>

          <Card>
            <Text style={styles.label}>آدرس سرور</Text>
            <Text style={styles.server} selectable>
              {server || 'همان مبدأ وب'}
            </Text>
          </Card>

          <Card>
            <Text style={styles.label}>کارهای در انتظار</Text>
            <Text style={styles.line}>
              {tasks.isLoading ? 'در حال دریافت...' : `${faDigits(pending)} کار برای اقدام شما`}
            </Text>
            <Button label="مشاهده کارها" variant="secondary" onPress={() => router.push('/(app)/tasks')} />
          </Card>

          <Button label="تغییر رمز عبور" variant="secondary" onPress={() => router.push('/(app)/change-password')} />
          <Button
            label={leaving ? 'در حال خروج...' : 'خروج از حساب'}
            variant="danger"
            loading={leaving}
            onPress={() => {
              setLeaving(true);
              void logout().finally(() => setLeaving(false));
            }}
          />
        </View>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { padding: space.md, gap: space.md },
  name: {
    color: colors.accent,
    fontSize: 22,
    fontWeight: '700',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  line: { color: colors.ink, fontSize: 15, textAlign: 'right', writingDirection: 'rtl' },
  label: {
    color: colors.muted,
    fontSize: 12,
    fontWeight: '700',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  server: {
    color: colors.ink,
    fontSize: 14,
    textAlign: 'left',
    writingDirection: 'ltr',
  },
  badge: {
    alignSelf: 'flex-end',
    color: colors.accent,
    backgroundColor: colors.accentSoft,
    overflow: 'hidden',
    paddingHorizontal: 10,
    paddingVertical: 4,
    fontSize: 12,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
});
