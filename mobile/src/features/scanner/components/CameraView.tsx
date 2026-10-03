import { useRef, useState } from 'react';
import { Linking, StyleSheet, View } from 'react-native';
import { Text } from '../../../components/AppText';
import { CameraView as ExpoCamera, useCameraPermissions } from 'expo-camera';
import { BrandMark } from '../../../components/BrandMark';
import { Button } from '../../../components/Button';
import { ErrorMessage } from '../../../components/ErrorMessage';
import { colors, space } from '../../../theme';
import { faDigits } from '../../../utils/format';

interface Props {
  pageCount: number;
  onCapture: (photo: { uri: string; width: number; height: number }) => Promise<void> | void;
  onImport: () => void;
  onSystemCamera?: () => void;
  onFinish: () => void;
}

export function CameraView({ pageCount, onCapture, onImport, onSystemCamera, onFinish }: Props) {
  const camera = useRef<ExpoCamera>(null);
  const [ready, setReady] = useState(false);
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

  async function takePhoto() {
    const view = camera.current;
    if (!view) throw new Error('camera_missing');

    try {
      return await view.takePictureAsync({ quality: 0.7, shutterSound: false });
    } catch {
      // Some Android devices fail while re-encoding the frame; raw capture still works.
      return await view.takePictureAsync({ skipProcessing: true, shutterSound: false });
    }
  }

  async function capture() {
    if (busy) return;
    if (!ready || !camera.current) {
      setError('دوربین هنوز آماده نیست. یک لحظه صبر کنید و دوباره بزنید.');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const photo = await takePhoto();
      if (!photo?.uri) {
        setError('عکس ذخیره نشد. دوباره تلاش کنید یا از گالری انتخاب کنید.');
        return;
      }
      await onCapture({
        uri: photo.uri,
        width: photo.width || 0,
        height: photo.height || 0,
      });
    } catch {
      setError('گرفتن عکس ناموفق بود. از «دوربین گوشی» یا گالری استفاده کنید.');
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
        onCameraReady={() => setReady(true)}
        onMountError={() => {
          setReady(false);
          setError('دوربین باز نشد. از «دوربین گوشی» یا گالری استفاده کنید.');
        }}
      />
      <View style={styles.bar}>
        <Text style={styles.count}>{pageCount === 0 ? 'هنوز صفحه‌ای گرفته نشده' : `${faDigits(pageCount)} صفحه`}</Text>
        <ErrorMessage message={error} />
        <Button
          label={busy ? 'در حال گرفتن عکس...' : ready ? 'گرفتن عکس' : 'آماده‌سازی دوربین...'}
          onPress={() => void capture()}
          loading={busy}
          disabled={!ready || busy}
        />
        <View style={styles.row}>
          <Button label="از گالری" variant="secondary" onPress={onImport} style={styles.flex} disabled={busy} />
          {onSystemCamera ? (
            <Button label="دوربین گوشی" variant="secondary" onPress={onSystemCamera} style={styles.flex} disabled={busy} />
          ) : null}
        </View>
        <Button label="پایان اسکن" variant="secondary" onPress={onFinish} disabled={busy || pageCount === 0} />
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
    borderTopWidth: 1,
    borderTopColor: colors.line,
  },
  count: { textAlign: 'center', color: colors.ink, fontWeight: '700', writingDirection: 'rtl' },
  row: { flexDirection: 'row', gap: space.sm },
  flex: { flex: 1 },
  permission: { flex: 1, justifyContent: 'center', alignItems: 'center', gap: space.md, padding: space.lg },
  permissionButton: { alignSelf: 'stretch' },
  permissionText: { color: colors.ink, fontSize: 16, lineHeight: 26, textAlign: 'center', writingDirection: 'rtl' },
});
