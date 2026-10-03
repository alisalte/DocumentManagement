import { StyleSheet, View, type TextInputProps } from 'react-native';
import { TextInput } from 'react-native-paper';
import { Text } from './AppText';
import { paperColors } from '../theme/paper';

interface Props extends Pick<TextInputProps, 'autoCapitalize' | 'autoCorrect' | 'secureTextEntry' | 'textContentType' | 'keyboardType'> {
  label: string;
  value: string;
  error?: string;
  onChangeText: (value: string) => void;
  onBlur: () => void;
  returnKeyType?: TextInputProps['returnKeyType'];
}

export function AuthField({ label, value, error, onChangeText, onBlur, ...props }: Props) {
  return (
    <View style={styles.wrap}>
      <TextInput
        mode="outlined"
        label={label}
        value={value}
        onChangeText={onChangeText}
        onBlur={onBlur}
        error={Boolean(error)}
        selectionColor={paperColors.primary}
        outlineColor="#D8DBE3"
        activeOutlineColor={paperColors.primary}
        textColor={paperColors.secondary}
        style={styles.input}
        contentStyle={styles.content}
        {...props}
      />
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { width: '100%', marginVertical: 8 },
  input: { width: '100%', backgroundColor: paperColors.surface },
  content: { fontFamily: 'Vazir', textAlign: 'right', writingDirection: 'rtl' },
  error: { color: paperColors.error, fontSize: 13, textAlign: 'right', writingDirection: 'rtl', paddingTop: 4, paddingHorizontal: 4 },
});
