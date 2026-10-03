import { ActivityIndicator, Pressable, StyleSheet, type StyleProp, type ViewStyle } from 'react-native';
import { Text } from './AppText';
import { colors, radius, space } from '../theme';

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost';

interface Props {
  label: string;
  onPress: () => void;
  variant?: Variant;
  disabled?: boolean;
  loading?: boolean;
  style?: StyleProp<ViewStyle>;
}

export function Button({ label, onPress, variant = 'primary', disabled, loading, style }: Props) {
  const blocked = disabled || loading;
  const onFill = variant === 'primary' || variant === 'danger';
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: blocked, busy: loading }}
      disabled={blocked}
      onPress={onPress}
      style={({ pressed }) => [
        styles.base,
        styles[variant],
        pressed && !blocked && variant === 'primary' ? styles.primaryPressed : null,
        pressed && !blocked && variant !== 'primary' ? styles.pressed : null,
        blocked ? styles.disabled : null,
        style,
      ]}
    >
      {loading ? <ActivityIndicator color={onFill ? '#fff' : colors.accent} /> : null}
      <Text style={[styles.label, onFill ? styles.labelOnFill : variant === 'ghost' ? styles.labelGhost : styles.labelOnSurface]}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: 56,
    borderRadius: radius.pill,
    paddingHorizontal: space.lg,
    paddingVertical: space.sm,
    alignItems: 'center',
    justifyContent: 'center',
    flexDirection: 'row',
    gap: space.xs,
  },
  primary: { backgroundColor: colors.accent },
  primaryPressed: { backgroundColor: colors.accentPressed },
  secondary: { backgroundColor: colors.card, borderWidth: 1, borderColor: colors.line },
  danger: { backgroundColor: colors.danger },
  ghost: { backgroundColor: 'transparent' },
  pressed: { opacity: 0.85 },
  disabled: { opacity: 0.45 },
  label: { fontSize: 17, fontWeight: '700', writingDirection: 'rtl' },
  labelOnFill: { color: '#fff' },
  labelOnSurface: { color: colors.ink },
  labelGhost: { color: colors.accent },
});
