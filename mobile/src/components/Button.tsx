import type { ComponentProps } from 'react';
import { StyleSheet, type StyleProp, type ViewStyle } from 'react-native';
import { Button as PaperButton } from 'react-native-paper';
import { colors, radius, shadow } from '../theme';

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost';

interface Props {
  label: string;
  onPress: () => void;
  variant?: Variant;
  disabled?: boolean;
  loading?: boolean;
  /** A MaterialCommunityIcons name shown before the label. */
  icon?: ComponentProps<typeof PaperButton>['icon'];
  style?: StyleProp<ViewStyle>;
}

export function Button({ label, onPress, variant = 'primary', disabled, loading, icon, style }: Props) {
  const mode = variant === 'secondary' ? 'outlined' : variant === 'ghost' ? 'text' : 'contained';
  const filled = variant === 'primary' || variant === 'danger';
  const blocked = disabled || loading;
  return (
    <PaperButton
      mode={mode}
      onPress={onPress}
      disabled={blocked}
      loading={loading}
      icon={icon}
      buttonColor={variant === 'danger' ? colors.danger : variant === 'primary' ? colors.accent : variant === 'secondary' ? colors.card : undefined}
      textColor={filled ? '#ffffff' : variant === 'secondary' ? colors.text : colors.accent}
      style={[
        styles.button,
        variant === 'secondary' && styles.outlined,
        variant === 'primary' && !blocked && shadow.brand,
        style,
      ]}
      contentStyle={styles.content}
      labelStyle={styles.label}
    >
      {label}
    </PaperButton>
  );
}

const styles = StyleSheet.create({
  button: { alignSelf: 'stretch', borderRadius: radius.md },
  outlined: { borderColor: colors.lineStrong },
  content: { minHeight: 50 },
  label: { fontFamily: 'Vazir-Bold', fontSize: 15, fontWeight: 'normal', writingDirection: 'rtl' },
});
