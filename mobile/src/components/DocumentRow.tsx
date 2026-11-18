import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { FileTypeBadge } from './FileTypeBadge';
import { colors, radius, shadow, space } from '../theme';
import { formatBytes, formatDateFa } from '../utils/format';

interface Props {
  title: string;
  subtitle?: string | null;
  updatedAt?: string | null;
  fileSize?: number | null;
  badge?: string | null;
  mimeType?: string | null;
  onPress: () => void;
}

export function DocumentRow({ title, subtitle, updatedAt, fileSize, badge, mimeType, onPress }: Props) {
  const meta = [formatDateFa(updatedAt), fileSize != null ? formatBytes(fileSize) : null].filter(Boolean).join(' · ');
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed && styles.pressed]}
    >
      <FileTypeBadge fileName={subtitle} mimeType={mimeType} />
      <View style={styles.body}>
        <Text style={styles.title} numberOfLines={2}>
          {title}
        </Text>
        {subtitle ? (
          <Text style={styles.subtitle} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
        <View style={styles.metaRow}>
          {badge ? (
            <View style={styles.badge}>
              <Text style={styles.badgeText}>{badge}</Text>
            </View>
          ) : null}
          <Text style={styles.meta} numberOfLines={1}>
            {meta}
          </Text>
        </View>
      </View>
      <MaterialCommunityIcons name="chevron-left" size={20} color={colors.subtle} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    backgroundColor: colors.card,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.line,
    paddingVertical: 14,
    paddingHorizontal: 14,
    ...shadow.card,
  },
  pressed: { backgroundColor: colors.accentSoft, borderColor: colors.accentLine },
  body: { flex: 1, gap: 3 },
  title: {
    color: colors.ink,
    fontSize: 15,
    fontWeight: '700',
    lineHeight: 23,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  subtitle: {
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
  },
  metaRow: { flexDirection: 'row', alignItems: 'center', gap: space.xs, marginTop: 2 },
  meta: {
    flexShrink: 1,
    color: colors.muted,
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  badge: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: colors.lineStrong,
    paddingHorizontal: 6,
    paddingVertical: 1,
  },
  badgeText: { color: colors.text, fontSize: 11, fontWeight: '600', writingDirection: 'ltr' },
});
