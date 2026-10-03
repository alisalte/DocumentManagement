import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { colors, space } from '../theme';

export function Loading({ label = 'در حال بارگذاری...' }: { label?: string }) {
  return (
    <View style={styles.wrap}>
      <ActivityIndicator color={colors.accent} size="large" />
      <Text style={styles.label}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: space.md, padding: space.lg },
  label: { color: colors.muted, fontSize: 15, fontWeight: '600', writingDirection: 'rtl', textAlign: 'center' },
});
