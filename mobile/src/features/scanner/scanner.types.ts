export interface ScanPage {
  id: string;
  uri: string;
  width: number;
  height: number;
}

export interface ScanSession {
  pages: ScanPage[];
  pdfUri?: string;
}
