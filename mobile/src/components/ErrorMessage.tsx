import { MaterialCommunityIcons } from '@expo/vector-icons';
import { StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { colors, radius, space } from '../theme';

export function ErrorMessage({ message }: { message?: string | null }) {
  if (!message) return null;
  return (
    <View style={styles.wrap} accessibilityRole="alert">
      <MaterialCommunityIcons name="alert-circle-outline" size={20} color={colors.danger} />
      <Text style={styles.text}>{message}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: space.sm,
    backgroundColor: colors.dangerSoft,
    borderWidth: 1,
    borderColor: '#fecdd3',
    borderRadius: radius.md,
    padding: 14,
  },
  text: { flex: 1, color: '#9f1239', fontSize: 14, lineHeight: 22, writingDirection: 'rtl', textAlign: 'right' },
});
