/** Browsers only expose getUserMedia in a secure context (HTTPS or localhost). */
export function isInsecureBrowserContext(
  secure: boolean | undefined = typeof window !== 'undefined' ? window.isSecureContext : undefined,
): boolean {
  if (typeof window === 'undefined' && secure === undefined) return false;
  return secure !== true;
}

export function cameraHttpsHint(href?: string): string {
  const current = href ?? (typeof window !== 'undefined' ? window.location.href : '');
  const httpsUrl = current.replace(/^http:\/\//i, 'https://');
  return `مرورگر گوشی روی http اجازهٔ دوربین نمی‌دهد. همین صفحه را با https باز کنید${
    httpsUrl && httpsUrl !== current ? `:\n${httpsUrl}` : '.'
  }\nاولین بار هشدار گواهی را بپذیرید، بعد اجازهٔ دوربین را تأیید کنید.`;
}
