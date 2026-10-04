export interface ScanPage {
  id: string;
  uri: string;
  width: number;
  height: number;
}

export type ScanOutputFormat = 'pdf' | 'image';

export interface PreparedScanFile {
  uri: string;
  fileName: string;
  mimeType: string;
  format: ScanOutputFormat;
}

export interface ScanSession {
  pages: ScanPage[];
  /** Cached file from the last successful prepare step (PDF or image). */
  prepared?: PreparedScanFile;
}
