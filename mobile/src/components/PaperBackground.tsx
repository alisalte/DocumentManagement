import type { ReactNode } from 'react';
import { Image, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View, type ViewStyle } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

const dot = require('../../assets/background-dot.png');
const dotUri =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACIAAAAiCAYAAAA6RwvCAAAATUlEQVR4nO3WoQ0AIBDAQGD/DT95yxiPwtRCAqI3QWV7VVX7wHgdsBlChpAhZAgZQoaQIXQcEjlb5Hwfckv30MAQMoQMIUPIEDKEDKEFM/wNe3JUz2MAAAAASUVORK5CYII=';

const webDots = (
  Platform.OS === 'web'
    ? {
        backgroundImage: `url("${dotUri}")`,
        backgroundRepeat: 'repeat',
        backgroundSize: '34px 34px',
      }
    : null
) as ViewStyle | null;

export function DottedFill({ children }: { children: ReactNode }) {
  return (
    <View style={[styles.background, webDots]}>
      {Platform.OS === 'web' ? null : <Image source={dot} resizeMode="repeat" style={styles.dots} />}
      {children}
    </View>
  );
}

export function PaperBackground({ children }: { children: ReactNode }) {
  return (
    <DottedFill>
      <SafeAreaView style={styles.flex}>
        <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
          <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
            <View style={styles.column}>{children}</View>
          </ScrollView>
        </KeyboardAvoidingView>
      </SafeAreaView>
    </DottedFill>
  );
}

const styles = StyleSheet.create({
  background: { flex: 1, width: '100%', backgroundColor: '#fff' },
  dots: StyleSheet.absoluteFill,
  flex: { flex: 1 },
  scroll: { flexGrow: 1, justifyContent: 'center', padding: 20 },
  column: { width: '100%', maxWidth: 340, alignSelf: 'center', alignItems: 'center' },
});
