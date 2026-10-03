import * as ImagePicker from 'expo-image-picker';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { ErrorMessage } from '../../src/components/ErrorMessage';
import { PageHeader } from '../../src/components/PageHeader';
import { CameraView } from '../../src/features/scanner/components/CameraView';
import { persistPage } from '../../src/features/scanner/persist-page';
import { useScanSession } from '../../src/features/scanner/ScanSessionProvider';
import { colors, space } from '../../src/theme';

export default function ScanScreen() {
  const router = useRouter();
  const session = useScanSession();
  const [error, setError] = useState<string | null>(null);

  async function addPhoto(photo: { uri: string; width: number; height: number }) {
    const page = await persistPage(photo);
    session.add(page);
  }

  async function importFromGallery() {
    setError(null);
    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      setError('دسترسی به گالری داده نشد.');
      return;
    }
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images'],
      quality: 0.7,
      allowsMultipleSelection: true,
      selectionLimit: 20,
    });
    if (result.canceled) return;
    for (const asset of result.assets) {
      await addPhoto({ uri: asset.uri, width: asset.width, height: asset.height });
    }
  }

  return (
    <SafeAreaView style={styles.safe} edges={['top', 'bottom']}>
      <PageHeader title="اسکن سند" onBack={() => router.back()} />
      {error ? (
        <View style={styles.error}>
          <ErrorMessage message={error} />
        </View>
      ) : null}
      <CameraView
        pageCount={session.pages.length}
        onCapture={addPhoto}
        onImport={() => void importFromGallery()}
        onFinish={() => router.push('/(app)/preview')}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  error: { paddingHorizontal: space.md },
});
