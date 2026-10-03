import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { colors, radius, space } from '../theme';
import { formatBytes, formatDateFa } from '../utils/format';

interface Props {
  title: string;
  subtitle?: string | null;
  updatedAt?: string | null;
  fileSize?: number | null;
  badge?: string | null;
  onPress: () => void;
}

export function DocumentRow({ title, subtitle, updatedAt, fileSize, badge, onPress }: Props) {
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.row, pressed && styles.pressed]}>
      <View style={styles.body}>
        <Text style={styles.title} numberOfLines={2}>
          {title}
        </Text>
        {subtitle ? (
          <Text style={styles.subtitle} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
        <Text style={styles.meta} numberOfLines={1}>
          {[formatDateFa(updatedAt), fileSize != null ? formatBytes(fileSize) : null].filter(Boolean).join(' · ')}
        </Text>
      </View>
      {badge ? (
        <View style={styles.badge}>
          <Text style={styles.badgeText}>{badge}</Text>
        </View>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
    backgroundColor: colors.card,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.line,
    padding: space.md,
  },
  pressed: { opacity: 0.85 },
  body: { flex: 1, gap: 4 },
  title: {
    color: colors.ink,
    fontSize: 16,
    fontWeight: '700',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  subtitle: {
    color: colors.muted,
    fontSize: 13,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  meta: {
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  badge: {
    backgroundColor: colors.accentSoft,
    borderRadius: radius.pill,
    paddingHorizontal: 10,
    paddingVertical: 6,
  },
  badgeText: { color: colors.accent, fontSize: 12, fontWeight: '700', writingDirection: 'rtl' },
});
