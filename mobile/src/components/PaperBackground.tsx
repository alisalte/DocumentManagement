import type { ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors } from '../theme';

/** The plain app surface every screen sits on. (The name is kept from the dotted template.) */
export function DottedFill({ children }: { children: ReactNode }) {
  return <View style={styles.background}>{children}</View>;
}

/** Centered, scrollable column for sign-in style screens, with a soft brand glow at the top. */
export function PaperBackground({ children }: { children: ReactNode }) {
  return (
    <View style={styles.background}>
      <View style={styles.glow} pointerEvents="none" />
      <SafeAreaView style={styles.flex}>
        <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
          <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
            <View style={styles.column}>{children}</View>
          </ScrollView>
        </KeyboardAvoidingView>
      </SafeAreaView>
    </View>
  );
}

const styles = StyleSheet.create({
  background: { flex: 1, width: '100%', backgroundColor: colors.background, overflow: 'hidden' },
  glow: {
    position: 'absolute',
    top: -220,
    left: -120,
    right: -120,
    height: 420,
    borderBottomLeftRadius: 400,
    borderBottomRightRadius: 400,
    backgroundColor: colors.accentSoft,
  },
  flex: { flex: 1 },
  scroll: { flexGrow: 1, justifyContent: 'center', padding: 20 },
  column: { width: '100%', maxWidth: 380, alignSelf: 'center', alignItems: 'center' },
});
