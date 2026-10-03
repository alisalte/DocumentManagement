import { StyleSheet, type StyleProp, type ViewStyle } from 'react-native';
import { Button as PaperButton } from 'react-native-paper';
import { paperColors } from '../theme/paper';

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
  const mode = variant === 'secondary' ? 'outlined' : variant === 'ghost' ? 'text' : 'contained';
  const filled = variant === 'primary' || variant === 'danger';
  return (
    <PaperButton
      mode={mode}
      onPress={onPress}
      disabled={disabled || loading}
      loading={loading}
      buttonColor={variant === 'danger' ? paperColors.error : variant === 'primary' ? paperColors.primary : undefined}
      textColor={filled ? '#ffffff' : paperColors.primary}
      style={[styles.button, style]}
      contentStyle={styles.content}
      labelStyle={styles.label}
    >
      {label}
    </PaperButton>
  );
}

const styles = StyleSheet.create({
  button: { alignSelf: 'stretch', borderRadius: 4 },
  content: { minHeight: 48 },
  label: { fontFamily: 'Vazir-Bold', fontSize: 15, fontWeight: 'normal', writingDirection: 'rtl' },
});
