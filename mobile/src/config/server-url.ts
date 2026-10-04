/**
 * Accepts a host, an origin, or a full API URL and returns the `/api/v1` base
 * the client calls. The phone types the same address that opened the web login.
 *
 * Ports 8091/8092 are the browser scanner (HTTPS / HTTP hint), not the API.
 * Those are rewritten to 8090 so a copied scanner address still works in the APK.
 */
export function normalizeServerUrl(input: string): string {
  const trimmed = input.trim();
  if (!trimmed) throw new Error('server url is empty');

  const withScheme = /^https?:\/\//i.test(trimmed) ? trimmed : `http://${trimmed}`;
  let url: URL;
  try {
    url = new URL(withScheme);
  } catch {
    throw new Error('server url is invalid');
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error('server url is invalid');
  if (url.username || url.password) throw new Error('server url is invalid');
  if (!url.hostname) throw new Error('server url is invalid');

  // Browser scanner ports — the archive API is on 8090 behind the web nginx.
  if (url.port === '8091' || url.port === '8092') {
    url.protocol = 'http:';
    url.port = '8090';
  }

  const path = url.pathname.replace(/\/+$/, '');
  if (path === '' || path === '/' || path === '/api') {
    url.pathname = '/api/v1';
  } else if (path !== '/api/v1') {
    throw new Error('server url is invalid');
  }

  url.search = '';
  url.hash = '';
  return url.toString().replace(/\/$/, '');
}
