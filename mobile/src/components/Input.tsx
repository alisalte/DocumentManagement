import { StyleSheet, Text, TextInput, View, type TextInputProps } from 'react-native';
import { colors, space } from '../theme';

interface Props extends TextInputProps {
  label: string;
  error?: string;
}

export function Input({ label, error, style, ...props }: Props) {
  return (
    <View style={styles.wrap}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        placeholderTextColor={colors.muted}
        style={[styles.input, error ? styles.inputError : null, style]}
        {...props}
      />
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: space.xs },
  label: { color: colors.ink, fontSize: 14, fontWeight: '600', writingDirection: 'rtl', textAlign: 'right' },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: colors.line,
    borderRadius: 12,
    paddingHorizontal: space.md,
    backgroundColor: colors.card,
    color: colors.ink,
    fontSize: 16,
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  inputError: { borderColor: colors.danger },
  error: { color: colors.danger, fontSize: 13, writingDirection: 'rtl', textAlign: 'right' },
});
