import { useRef, useState } from 'react';
import { Linking, StyleSheet, Text, View } from 'react-native';
import { CameraView as ExpoCamera, useCameraPermissions } from 'expo-camera';
import { BrandMark } from '../../../components/BrandMark';
import { Button } from '../../../components/Button';
import { ErrorMessage } from '../../../components/ErrorMessage';
import { colors, radius, space } from '../../../theme';
import { faDigits } from '../../../utils/format';

interface Props {
  pageCount: number;
  onCapture: (photo: { uri: string; width: number; height: number }) => Promise<void> | void;
  onImport: () => void;
  onFinish: () => void;
}

export function CameraView({ pageCount, onCapture, onImport, onFinish }: Props) {
  const camera = useRef<ExpoCamera>(null);
  const ready = useRef(false);
  const [permission, requestPermission] = useCameraPermissions();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!permission) {
    return <View style={styles.fill} />;
  }

  if (!permission.granted) {
    return (
      <View style={styles.permission}>
        <BrandMark size={64} />
        <Text style={styles.permissionText}>
          {permission.canAskAgain
            ? 'برای اسکن سند به دوربین نیاز است.'
            : 'دسترسی دوربین بسته است. آن را از تنظیمات گوشی باز کنید.'}
        </Text>
        {permission.canAskAgain ? (
          <Button label="اجازه دوربین" onPress={() => void requestPermission()} style={styles.permissionButton} />
        ) : (
          <Button label="باز کردن تنظیمات" onPress={() => void Linking.openSettings()} style={styles.permissionButton} />
        )}
      </View>
    );
  }

  async function capture() {
    if (!ready.current || !camera.current || busy) return;
    setBusy(true);
    setError(null);
    try {
      const photo = await camera.current.takePictureAsync({ quality: 0.7 });
      if (!photo?.uri) {
        setError('عکس ذخیره نشد. دوباره تلاش کنید.');
        return;
      }
      await onCapture({ uri: photo.uri, width: photo.width, height: photo.height });
    } catch {
      setError('گرفتن عکس ناموفق بود. دوباره تلاش کنید.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={styles.fill}>
      <ExpoCamera
        ref={camera}
        style={styles.camera}
        facing="back"
        mode="picture"
        onCameraReady={() => {
          ready.current = true;
        }}
      />
      <View style={styles.bar}>
        <Text style={styles.count}>{pageCount === 0 ? 'هنوز صفحه‌ای گرفته نشده' : `${faDigits(pageCount)} صفحه`}</Text>
        <ErrorMessage message={error} />
        <Button label={busy ? 'در حال گرفتن عکس...' : 'گرفتن عکس'} onPress={() => void capture()} loading={busy} />
        <View style={styles.row}>
          <Button label="از گالری" variant="secondary" onPress={onImport} style={styles.flex} disabled={busy} />
          <Button label="پایان اسکن" variant="secondary" onPress={onFinish} style={styles.flex} disabled={busy || pageCount === 0} />
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  camera: { flex: 1, backgroundColor: '#111' },
  bar: {
    gap: space.sm,
    padding: space.md,
    paddingBottom: space.lg,
    backgroundColor: colors.card,
    borderTopLeftRadius: radius.lg,
    borderTopRightRadius: radius.lg,
    marginTop: -24,
  },
  count: { textAlign: 'center', color: colors.ink, fontWeight: '700', writingDirection: 'rtl' },
  row: { flexDirection: 'row', gap: space.sm },
  flex: { flex: 1 },
  permission: { flex: 1, justifyContent: 'center', alignItems: 'center', gap: space.md, padding: space.lg },
  permissionButton: { alignSelf: 'stretch' },
  permissionText: { color: colors.ink, fontSize: 16, lineHeight: 26, textAlign: 'center', writingDirection: 'rtl' },
});
