import { StyleSheet, View, type TextInputProps } from 'react-native';
import { TextInput } from 'react-native-paper';
import { Text } from './AppText';
import { paperColors } from '../theme/paper';

interface Props extends Pick<
  TextInputProps,
  'autoCapitalize' | 'autoCorrect' | 'secureTextEntry' | 'textContentType' | 'keyboardType' | 'value' | 'onChangeText' | 'onBlur' | 'placeholder' | 'multiline'
> {
  label: string;
  error?: string;
}

export function Input({ label, error, multiline, ...props }: Props) {
  return (
    <View style={styles.wrap}>
      <TextInput
        mode="outlined"
        label={label}
        error={Boolean(error)}
        multiline={multiline}
        selectionColor={paperColors.primary}
        outlineColor="#D8DBE3"
        activeOutlineColor={paperColors.primary}
        textColor={paperColors.secondary}
        style={[styles.input, multiline ? styles.multiline : null]}
        contentStyle={styles.content}
        {...props}
      />
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { width: '100%', marginVertical: 6 },
  input: { backgroundColor: paperColors.surface },
  multiline: { minHeight: 96 },
  content: { fontFamily: 'Vazir', textAlign: 'right' as const, writingDirection: 'rtl' as const },
  error: { color: paperColors.error, fontSize: 13, textAlign: 'right' as const, writingDirection: 'rtl' as const, paddingTop: 4, paddingHorizontal: 4 },
});
