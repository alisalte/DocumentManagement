import { StyleSheet, View } from 'react-native';
import { colors } from '../theme';

/** Abstract mark in the template's purple-and-orange palette. Not their logo. */
export function BrandMark({ size = 72 }: { size?: number }) {
  return (
    <View style={[styles.wrap, { width: size, height: size }]}>
      <View
        style={[
          styles.blob,
          {
            width: size * 0.62,
            height: size * 0.4,
            borderRadius: size * 0.2,
            backgroundColor: colors.accent,
            transform: [{ rotate: '-16deg' }, { translateX: -size * 0.06 }],
          },
        ]}
      />
      <View
        style={[
          styles.blob,
          {
            width: size * 0.46,
            height: size * 0.32,
            borderRadius: size * 0.16,
            backgroundColor: colors.warning,
            transform: [{ rotate: '24deg' }, { translateX: size * 0.18 }, { translateY: size * 0.08 }],
          },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { alignItems: 'center', justifyContent: 'center' },
  blob: { position: 'absolute' },
});
