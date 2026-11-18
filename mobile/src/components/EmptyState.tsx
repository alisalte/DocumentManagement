import { MaterialCommunityIcons } from '@expo/vector-icons';
import type { ComponentProps } from 'react';
import { StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { Button } from './Button';
import { colors, radius, space } from '../theme';

interface Props {
  title: string;
  message: string;
  actionLabel?: string;
  onAction?: () => void;
  icon?: ComponentProps<typeof MaterialCommunityIcons>['name'];
}

export function EmptyState({ title, message, actionLabel, onAction, icon = 'file-document-outline' }: Props) {
  return (
    <View style={styles.wrap}>
      <View style={styles.iconTile}>
        <MaterialCommunityIcons name={icon} size={30} color={colors.subtle} />
      </View>
      <Text style={styles.title}>{title}</Text>
      <Text style={styles.message}>{message}</Text>
      {actionLabel && onAction ? <Button label={actionLabel} onPress={onAction} style={styles.action} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexGrow: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: space.xs,
    paddingHorizontal: space.lg,
    paddingVertical: space.xl,
  },
  iconTile: {
    width: 64,
    height: 64,
    borderRadius: radius.lg + 4,
    backgroundColor: colors.card,
    borderWidth: 1,
    borderColor: colors.line,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: space.sm,
  },
  title: {
    color: colors.ink,
    fontSize: 17,
    fontWeight: '700',
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  message: {
    color: colors.muted,
    fontSize: 14,
    lineHeight: 23,
    textAlign: 'center',
    writingDirection: 'rtl',
    maxWidth: 300,
    marginBottom: space.sm,
  },
  action: { alignSelf: 'center', minWidth: 180, marginTop: space.xs },
});
