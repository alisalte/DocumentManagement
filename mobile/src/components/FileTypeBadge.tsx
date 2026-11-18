import { StyleSheet, View } from 'react-native';
import { Text } from './AppText';
import { radius } from '../theme';

const tones: Record<string, { bg: string; fg: string; line: string }> = {
  pdf: { bg: '#fff1f2', fg: '#be123c', line: '#ffe4e6' },
  doc: { bg: '#f0f9ff', fg: '#0369a1', line: '#e0f2fe' },
  sheet: { bg: '#ecfdf5', fg: '#047857', line: '#d1fae5' },
  image: { bg: '#f5f3ff', fg: '#6d28d9', line: '#ede9fe' },
  slide: { bg: '#fff7ed', fg: '#c2410c', line: '#ffedd5' },
  other: { bg: '#f1f5f9', fg: '#475569', line: '#e2e8f0' },
};

export function extensionOf(fileName?: string | null, mimeType?: string | null): string {
  const fromName = fileName?.includes('.') ? fileName.split('.').pop()?.toLowerCase() : undefined;
  if (fromName && fromName.length <= 5) return fromName;
  const mime = mimeType?.toLowerCase() ?? '';
  if (mime.includes('pdf')) return 'pdf';
  if (mime.includes('word')) return 'docx';
  if (mime.includes('sheet') || mime.includes('excel')) return 'xlsx';
  if (mime.includes('presentation') || mime.includes('powerpoint')) return 'pptx';
  if (mime.startsWith('image/')) return mime.split('/')[1]?.replace('jpeg', 'jpg') ?? 'img';
  return 'file';
}

function toneOf(ext: string) {
  if (ext === 'pdf') return tones.pdf;
  if (ext === 'doc' || ext === 'docx') return tones.doc;
  if (ext === 'xls' || ext === 'xlsx' || ext === 'csv') return tones.sheet;
  if (['png', 'jpg', 'jpeg', 'webp', 'gif', 'heic', 'tif', 'tiff'].includes(ext)) return tones.image;
  if (ext === 'ppt' || ext === 'pptx') return tones.slide;
  return tones.other;
}

/** Short colored extension tile, the same as the web document list. */
export function FileTypeBadge({ fileName, mimeType, size = 40 }: { fileName?: string | null; mimeType?: string | null; size?: number }) {
  const ext = extensionOf(fileName, mimeType);
  const tone = toneOf(ext)!;
  return (
    <View style={[styles.tile, { width: size, height: size, backgroundColor: tone.bg, borderColor: tone.line }]}>
      <Text style={[styles.text, { color: tone.fg }]}>{ext.slice(0, 4).toUpperCase()}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  tile: { borderRadius: radius.sm + 2, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  text: { fontSize: 10, fontWeight: '700', letterSpacing: 0.4, writingDirection: 'ltr' },
});
