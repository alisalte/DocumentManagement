import { StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { colors, space } from '../theme';

export function ErrorMessage({ message }: { message?: string | null }) {
  if (!message) return null;
  return (
    <View style={styles.wrap}>
      <Text style={styles.text}>{message}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    backgroundColor: colors.dangerSoft,
    borderRadius: 4,
    padding: space.md,
  },
  text: { color: colors.danger, fontSize: 14, fontWeight: '600', writingDirection: 'rtl', textAlign: 'right' },
});
