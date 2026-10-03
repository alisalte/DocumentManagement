import { StyleSheet, View, type TextInputProps } from 'react-native';
import { Text, TextInput } from './AppText';
import { colors, radius, space } from '../theme';

interface Props extends TextInputProps {
  label: string;
  error?: string;
}

export function Input({ label, error, style, multiline, ...props }: Props) {
  return (
    <View style={styles.wrap}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        placeholderTextColor={colors.muted}
        multiline={multiline}
        style={[styles.input, multiline ? styles.multiline : null, error ? styles.inputError : null, style]}
        {...props}
      />
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: space.xs },
  label: { color: colors.muted, fontSize: 13, fontWeight: '600', writingDirection: 'rtl', textAlign: 'right' },
  input: {
    minHeight: 54,
    borderWidth: 0,
    borderRadius: radius.md,
    paddingHorizontal: space.md,
    backgroundColor: '#EEF0F6',
    color: colors.ink,
    fontSize: 16,
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  multiline: { minHeight: 96, paddingTop: space.md, textAlignVertical: 'top' },
  inputError: { borderWidth: 1.5, borderColor: colors.danger },
  error: { color: colors.danger, fontSize: 13, writingDirection: 'rtl', textAlign: 'right' },
});
