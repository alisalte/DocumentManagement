import { MaterialCommunityIcons } from '@expo/vector-icons';
import { useState } from 'react';
import { Pressable, StyleSheet, View, type TextInputProps } from 'react-native';
import { TextInput } from './AppText';
import { colors, radius, shadow } from '../theme';

interface Props extends Pick<TextInputProps, 'onSubmitEditing' | 'returnKeyType' | 'autoFocus'> {
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  /** Accessible name when the placeholder is not enough. */
  label?: string;
}

/** Rounded search box with an icon and a clear button, like the web app's search. */
export function SearchField({ value, onChangeText, placeholder, label, ...props }: Props) {
  const [focused, setFocused] = useState(false);
  return (
    <View style={[styles.wrap, focused && styles.focused]}>
      <MaterialCommunityIcons name="magnify" size={20} color={focused ? colors.accent : colors.subtle} />
      <TextInput
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor={colors.subtle}
        accessibilityLabel={label ?? placeholder}
        autoCapitalize="none"
        autoCorrect={false}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        style={styles.input}
        {...props}
      />
      {value ? (
        <Pressable accessibilityRole="button" accessibilityLabel="پاک کردن" hitSlop={10} onPress={() => onChangeText('')}>
          <MaterialCommunityIcons name="close-circle" size={18} color={colors.subtle} />
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    height: 48,
    paddingHorizontal: 14,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.lineStrong,
    backgroundColor: colors.card,
    ...shadow.card,
  },
  focused: { borderColor: colors.accent },
  input: {
    flex: 1,
    height: '100%',
    color: colors.ink,
    fontSize: 15,
    textAlign: 'right',
    writingDirection: 'rtl',
    // The web input draws its own focus ring; the wrapper shows focus instead.
    outlineStyle: 'none',
  } as never,
});
