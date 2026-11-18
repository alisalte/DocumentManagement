import { MaterialCommunityIcons } from '@expo/vector-icons';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { colors, radius, space } from '../theme';

interface Props {
  title: string;
  /** One short line under the title. */
  subtitle?: string;
  onBack?: () => void;
  actionLabel?: string;
  onAction?: () => void;
}

/** A large, start-aligned screen title with an optional back button and a trailing action. */
export function PageHeader({ title, subtitle, onBack, actionLabel, onAction }: Props) {
  return (
    <View style={styles.row}>
      {onBack ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="بازگشت"
          onPress={onBack}
          hitSlop={10}
          style={({ pressed }) => [styles.back, pressed && styles.backPressed]}
        >
          <MaterialCommunityIcons name="arrow-right" size={22} color={colors.ink} />
        </Pressable>
      ) : null}
      <View style={styles.titles}>
        <Text style={[styles.title, onBack && styles.titleSmall]} numberOfLines={1}>
          {title}
        </Text>
        {subtitle ? (
          <Text style={styles.subtitle} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      {actionLabel && onAction ? (
        <Pressable
          accessibilityRole="button"
          onPress={onAction}
          hitSlop={8}
          style={({ pressed }) => [styles.action, pressed && styles.actionPressed]}
        >
          <Text style={styles.actionText}>{actionLabel}</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
    paddingHorizontal: space.md,
    paddingTop: space.md,
    paddingBottom: space.sm,
  },
  back: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.card,
    borderWidth: 1,
    borderColor: colors.line,
  },
  backPressed: { backgroundColor: colors.accentSoft },
  titles: { flex: 1, gap: 2 },
  title: {
    color: colors.ink,
    fontSize: 24,
    fontWeight: '800',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  titleSmall: { fontSize: 19 },
  subtitle: { color: colors.muted, fontSize: 13, textAlign: 'right', writingDirection: 'rtl' },
  action: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: radius.md,
    backgroundColor: colors.accentSoft,
  },
  actionPressed: { opacity: 0.8 },
  actionText: { color: colors.accent, fontSize: 14, fontWeight: '700', writingDirection: 'rtl' },
});
