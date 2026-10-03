import { Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, space } from '../theme';

interface Props {
  title: string;
  onBack?: () => void;
  actionLabel?: string;
  onAction?: () => void;
}

export function PageHeader({ title, onBack, actionLabel, onAction }: Props) {
  return (
    <View style={styles.row}>
      {onBack ? (
        <Pressable accessibilityRole="button" onPress={onBack} hitSlop={8} style={styles.side}>
          <Text style={styles.back}>بازگشت</Text>
        </Pressable>
      ) : (
        <View style={styles.side} />
      )}
      <Text style={styles.title} numberOfLines={1}>
        {title}
      </Text>
      {actionLabel && onAction ? (
        <Pressable accessibilityRole="button" onPress={onAction} hitSlop={8} style={styles.side}>
          <Text style={styles.action}>{actionLabel}</Text>
        </Pressable>
      ) : (
        <View style={styles.side} />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
    paddingHorizontal: space.md,
    paddingVertical: space.sm,
  },
  side: { minWidth: 64 },
  title: {
    flex: 1,
    textAlign: 'center',
    color: colors.ink,
    fontSize: 18,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  back: { color: colors.accent, fontSize: 15, writingDirection: 'rtl', textAlign: 'right' },
  action: { color: colors.accent, fontSize: 15, writingDirection: 'rtl', textAlign: 'left' },
});
