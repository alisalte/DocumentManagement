import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useState, type ComponentProps } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
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
import { colors, radius, space } from '../../../src/theme';
import { faDigits } from '../../../src/utils/format';

type IconName = ComponentProps<typeof MaterialCommunityIcons>['name'];

function Row({
  icon,
  title,
  detail,
  count,
  onPress,
  ltrDetail,
}: {
  icon: IconName;
  title: string;
  detail?: string;
  count?: number;
  onPress?: () => void;
  ltrDetail?: boolean;
}) {
  const body = (
    <>
      <View style={styles.rowIcon}>
        <MaterialCommunityIcons name={icon} size={20} color={colors.accent} />
      </View>
      <View style={styles.rowBody}>
        <Text style={styles.rowTitle}>{title}</Text>
        {detail ? (
          <Text style={[styles.rowDetail, ltrDetail && styles.ltr]} numberOfLines={1} selectable={!onPress}>
            {detail}
          </Text>
        ) : null}
      </View>
      {count ? (
        <View style={styles.count}>
          <Text style={styles.countText}>{faDigits(count)}</Text>
        </View>
      ) : null}
      {onPress ? <MaterialCommunityIcons name="chevron-left" size={20} color={colors.subtle} /> : null}
    </>
  );
  if (!onPress) return <View style={styles.row}>{body}</View>;
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.row, pressed && styles.rowPressed]}>
      {body}
    </Pressable>
  );
}

export default function AccountScreen() {
  const { user, logout } = useAuth();
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);
  const tasks = useQuery({ queryKey: ['workflow-tasks'], queryFn: listTasks, enabled: !isDemoPreview() });
  const pending = (tasks.data ?? []).filter((task) => task.status === 'Pending' && task.canAct).length;
  const server = currentApiUrl();
  const name = user?.displayName || user?.username || 'کاربر';

  return (
    <DottedFill>
      <SafeAreaView style={styles.safe} edges={['top']}>
        <PageHeader title="حساب کاربری" />
        <ScrollView contentContainerStyle={styles.content}>
          <Card style={styles.profile}>
            <View style={styles.avatar}>
              <Text style={styles.avatarText}>{name.slice(0, 1)}</Text>
            </View>
            <View style={styles.profileBody}>
              <Text style={styles.name} numberOfLines={1}>
                {name}
              </Text>
              <Text style={styles.username} numberOfLines={1}>
                {user?.username}
              </Text>
            </View>
            {user?.isSystemAdmin ? (
              <View style={styles.badge}>
                <Text style={styles.badgeText}>مدیر سیستم</Text>
              </View>
            ) : null}
          </Card>

          <Text style={styles.section}>کارها</Text>
          <View style={styles.group}>
            <Row
              icon="inbox-outline"
              title="کارتابل"
              detail={tasks.isLoading ? 'در حال دریافت…' : pending > 0 ? `${faDigits(pending)} کار برای اقدام شما` : 'کاری در انتظار نیست'}
              count={pending}
              onPress={() => router.push('/(app)/tasks')}
            />
          </View>

          <Text style={styles.section}>حساب و اتصال</Text>
          <View style={styles.group}>
            <Row icon="lock-reset" title="تغییر رمز عبور" onPress={() => router.push('/(app)/change-password')} />
            <View style={styles.divider} />
            <Row icon="server-network" title="آدرس سرور" detail={server || 'همان مبدأ وب'} ltrDetail={Boolean(server)} />
          </View>

          <Button
            label={leaving ? 'در حال خروج…' : 'خروج از حساب'}
            variant="secondary"
            icon="logout"
            loading={leaving}
            style={styles.logout}
            onPress={() => {
              setLeaving(true);
              void logout().finally(() => setLeaving(false));
            }}
          />
        </ScrollView>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { padding: space.md, paddingTop: space.sm, gap: space.sm },
  profile: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  avatar: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarText: { color: '#ffffff', fontSize: 22, fontWeight: '700' },
  profileBody: { flex: 1, gap: 2 },
  name: { color: colors.ink, fontSize: 18, fontWeight: '800', textAlign: 'right', writingDirection: 'rtl' },
  username: { color: colors.muted, fontSize: 13, textAlign: 'right' },
  badge: { backgroundColor: colors.accentSoft, borderRadius: radius.pill, paddingHorizontal: 10, paddingVertical: 4 },
  badgeText: { color: colors.accent, fontSize: 12, fontWeight: '700', writingDirection: 'rtl' },
  section: {
    marginTop: space.sm,
    paddingHorizontal: 4,
    color: colors.muted,
    fontSize: 12,
    fontWeight: '700',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  group: {
    backgroundColor: colors.card,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.line,
    overflow: 'hidden',
  },
  divider: { height: 1, backgroundColor: colors.line, marginHorizontal: 14 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 14, paddingHorizontal: 14 },
  rowPressed: { backgroundColor: colors.accentSoft },
  rowIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.md,
    backgroundColor: colors.accentSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  rowBody: { flex: 1, gap: 2 },
  rowTitle: { color: colors.ink, fontSize: 15, fontWeight: '600', textAlign: 'right', writingDirection: 'rtl' },
  rowDetail: { color: colors.muted, fontSize: 12, textAlign: 'right', writingDirection: 'rtl' },
  ltr: { writingDirection: 'ltr' },
  count: { minWidth: 24, borderRadius: radius.pill, backgroundColor: colors.accent, paddingHorizontal: 7, paddingVertical: 2, alignItems: 'center' },
  countText: { color: '#ffffff', fontSize: 12, fontWeight: '700' },
  logout: { marginTop: space.md },
});
