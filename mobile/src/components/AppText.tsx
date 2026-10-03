import { StyleSheet, Text as RNText, TextInput as RNTextInput, type TextInputProps, type TextProps, type TextStyle } from 'react-native';
import { vazirFamily } from '../fonts';

function vazirStyle(style: TextProps['style']): TextStyle {
  const flat = StyleSheet.flatten(style) as TextStyle | undefined;
  return { fontFamily: vazirFamily(flat?.fontWeight), fontWeight: 'normal' };
}

export function Text({ style, ...props }: TextProps) {
  return <RNText {...props} style={[style, vazirStyle(style)]} />;
}

export function TextInput({ style, ...props }: TextInputProps) {
  return <RNTextInput {...props} style={[style, vazirStyle(style)]} />;
}
