export interface PdfImage {
  dataUri: string;
}

/** One section per page. The last page does not force a trailing blank page. */
export function buildPdfHtml(images: PdfImage[]): string {
  const pages = images
    .map(
      (image, index) =>
        `<section class="page"><img src="${image.dataUri}" alt="page ${index + 1}" /></section>`,
    )
    .join('');

  return `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    @page { size: A4; margin: 8mm; }
    html, body { margin: 0; padding: 0; }
    .page { break-after: page; page-break-after: always; }
    .page:last-child { break-after: auto; page-break-after: auto; }
    img { width: 100%; height: auto; display: block; }
  </style>
</head>
<body>${pages}</body>
</html>`;
}

export function imageMime(uri: string): 'image/png' | 'image/webp' | 'image/jpeg' {
  const path = uri.split('?')[0]?.toLowerCase() ?? '';
  if (path.endsWith('.png')) return 'image/png';
  if (path.endsWith('.webp')) return 'image/webp';
  return 'image/jpeg';
}
