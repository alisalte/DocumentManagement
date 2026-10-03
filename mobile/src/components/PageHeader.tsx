import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './AppText';
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
        <Pressable accessibilityRole="button" accessibilityLabel="بازگشت" onPress={onBack} hitSlop={10} style={styles.side}>
          <Text style={styles.back}>→</Text>
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
    paddingVertical: space.md,
  },
  side: { minWidth: 48, alignItems: 'center' },
  title: {
    flex: 1,
    textAlign: 'center',
    color: colors.accent,
    fontSize: 18,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  back: { color: colors.ink, fontSize: 26, lineHeight: 28 },
  action: { color: colors.accent, fontSize: 15, fontWeight: '700', writingDirection: 'rtl', textAlign: 'left' },
});
