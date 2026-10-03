import { Image, Pressable, StyleSheet, View } from 'react-native';
import { Text } from '../../../components/AppText';
import { colors, radius, space } from '../../../theme';
import { faDigits } from '../../../utils/format';
import type { ScanPage } from '../scanner.types';

interface Props {
  page: ScanPage;
  index: number;
  canMoveEarlier: boolean;
  canMoveLater: boolean;
  onMoveEarlier: () => void;
  onMoveLater: () => void;
  onDelete: () => void;
}

export function PageThumbnail({
  page,
  index,
  canMoveEarlier,
  canMoveLater,
  onMoveEarlier,
  onMoveLater,
  onDelete,
}: Props) {
  return (
    <View style={styles.card}>
      <Image source={{ uri: page.uri }} style={styles.image} resizeMode="cover" />
      <Text style={styles.caption}>صفحه {faDigits(index + 1)}</Text>
      <View style={styles.actions}>
        <TextAction label="قبل" disabled={!canMoveEarlier} onPress={onMoveEarlier} />
        <TextAction label="بعد" disabled={!canMoveLater} onPress={onMoveLater} />
        <TextAction label="حذف" onPress={onDelete} danger />
      </View>
    </View>
  );
}

function TextAction({
  label,
  onPress,
  disabled,
  danger,
}: {
  label: string;
  onPress: () => void;
  disabled?: boolean;
  danger?: boolean;
}) {
  return (
    <Pressable accessibilityRole="button" disabled={disabled} onPress={onPress} hitSlop={6}>
      <Text style={[styles.action, danger ? styles.danger : null, disabled ? styles.disabled : null]}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    width: '48%',
    backgroundColor: colors.card,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.line,
    padding: space.sm,
    gap: space.xs,
  },
  image: { width: '100%', aspectRatio: 0.75, borderRadius: radius.sm, backgroundColor: colors.line },
  caption: { textAlign: 'center', color: colors.ink, fontWeight: '700', writingDirection: 'rtl' },
  actions: { flexDirection: 'row', justifyContent: 'space-between' },
  action: { color: colors.accent, fontSize: 13, fontWeight: '700', writingDirection: 'rtl' },
  danger: { color: colors.danger },
  disabled: { opacity: 0.35 },
});
