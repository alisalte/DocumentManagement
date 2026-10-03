import { useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { downloadHostIsLocal, scannerDownloadUrl } from '../lib/apk-download';

export function ScannerDownload() {
  const origin = window.location.origin;
  const url = scannerDownloadUrl(origin);
  const local = downloadHostIsLocal(window.location.hostname);
  const [src, setSrc] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void QRCode.toDataURL(url, { margin: 1, width: 168, errorCorrectionLevel: 'M' }).then((dataUrl) => {
      if (!cancelled) setSrc(dataUrl);
    });
    return () => {
      cancelled = true;
    };
  }, [url]);

  return (
    <section className="mt-4 rounded-2xl border border-paper-200 bg-paper-50 p-4 text-center shadow-[0_8px_24px_rgb(31_22_56/0.08)]">
      <p className="text-sm font-semibold text-ink-900">دانلود اپ اسکنر</p>
      <p className="mt-1 text-xs leading-5 text-paper-500">با دوربین گوشی این کد را اسکن کنید تا برنامه دانلود شود.</p>
      {src && (
        <img
          src={src}
          alt="کد دانلود اپ اندروید"
          width={168}
          height={168}
          className="mx-auto mt-3 rounded-lg bg-white p-2"
        />
      )}
      <a href={url} className="mt-3 inline-block text-sm font-medium text-ink-700 underline">
        دانلود مستقیم APK
      </a>
      {local && (
        <p className="mt-2 text-xs leading-5 text-copper-600">
          گوشی به localhost دسترسی ندارد. این صفحه را با آی‌پی شبکه باز کنید، مثلاً http://192.168.1.10:8090
        </p>
      )}
      <p className="mt-2 text-xs leading-5 text-paper-500">بعد از نصب، همین آدرس را در برنامه وارد کنید: {origin}</p>
    </section>
  );
}
