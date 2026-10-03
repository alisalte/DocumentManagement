import { StyleSheet, View } from 'react-native';
import { space } from '../../../theme';
import type { ScanPage } from '../scanner.types';
import { PageThumbnail } from './PageThumbnail';

interface Props {
  pages: ScanPage[];
  onMove: (id: string, direction: -1 | 1) => void;
  onDelete: (id: string) => void;
}

export function ScanPreview({ pages, onMove, onDelete }: Props) {
  return (
    <View style={styles.grid}>
      {pages.map((page, index) => (
        <PageThumbnail
          key={page.id}
          page={page}
          index={index}
          canMoveEarlier={index > 0}
          canMoveLater={index < pages.length - 1}
          onMoveEarlier={() => onMove(page.id, -1)}
          onMoveLater={() => onMove(page.id, 1)}
          onDelete={() => onDelete(page.id)}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: space.md, justifyContent: 'space-between' },
});
