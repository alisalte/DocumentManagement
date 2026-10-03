import type { TextStyle } from 'react-native';

/** Vazirmatn files, registered under the Vazir family. Weight picks the face. */
export function vazirFamily(weight?: TextStyle['fontWeight']): string {
  switch (String(weight ?? '400')) {
    case '500':
      return 'Vazir-Medium';
    case '600':
      return 'Vazir-SemiBold';
    case '700':
    case 'bold':
      return 'Vazir-Bold';
    case '800':
    case '900':
      return 'Vazir-ExtraBold';
    default:
      return 'Vazir';
  }
}
