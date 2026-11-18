import { MaterialCommunityIcons } from '@expo/vector-icons';
import { StyleSheet, View } from 'react-native';
import { colors, shadow } from '../theme';

/** The app mark: a folder on an indigo tile, the same as the web app's sidebar. */
export function BrandMark({ size = 72 }: { size?: number }) {
  return (
    <View
      style={[
        styles.tile,
        { width: size, height: size, borderRadius: Math.round(size * 0.3) },
      ]}
    >
      <View style={[styles.shine, { height: size / 2 }]} />
      <MaterialCommunityIcons name="folder-text-outline" size={Math.round(size * 0.52)} color="#ffffff" />
    </View>
  );
}

const styles = StyleSheet.create({
  tile: {
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.accent,
    overflow: 'hidden',
    ...shadow.brand,
  },
  // Lighter top half, a hint of depth without a gradient library.
  shine: { position: 'absolute', top: 0, left: 0, right: 0, backgroundColor: 'rgba(255,255,255,0.1)' },
});
