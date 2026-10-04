const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

/** Decode a base64 string into raw bytes. */
export function decodeBase64(base64: string): Uint8Array {
  const normalized = base64.replace(/^data:[^;]+;base64,/, '').replace(/\s+/g, '');
  if (typeof globalThis.atob === 'function') {
    const binary = globalThis.atob(normalized);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
    return bytes;
  }

  const cleaned = normalized.replace(/=+$/, '');
  const output = new Uint8Array(Math.floor((cleaned.length * 3) / 4));
  let out = 0;
  for (let i = 0; i < cleaned.length; i += 4) {
    const a = alphabet.indexOf(cleaned[i]!);
    const b = alphabet.indexOf(cleaned[i + 1]!);
    const c = alphabet.indexOf(cleaned[i + 2] ?? 'A');
    const d = alphabet.indexOf(cleaned[i + 3] ?? 'A');
    const triad = (a << 18) | (b << 12) | (c << 6) | d;
    if (out < output.length) output[out++] = (triad >> 16) & 0xff;
    if (out < output.length) output[out++] = (triad >> 8) & 0xff;
    if (out < output.length) output[out++] = triad & 0xff;
  }
  return output;
}

export function encodeBase64(bytes: Uint8Array): string {
  if (typeof globalThis.btoa === 'function') {
    let binary = '';
    for (let i = 0; i < bytes.length; i += 1) binary += String.fromCharCode(bytes[i]!);
    return globalThis.btoa(binary);
  }

  let result = '';
  for (let i = 0; i < bytes.length; i += 3) {
    const a = bytes[i]!;
    const b = bytes[i + 1];
    const c = bytes[i + 2];
    const triad = (a << 16) | ((b ?? 0) << 8) | (c ?? 0);
    result += alphabet[(triad >> 18) & 63];
    result += alphabet[(triad >> 12) & 63];
    result += b === undefined ? '=' : alphabet[(triad >> 6) & 63];
    result += c === undefined ? '=' : alphabet[triad & 63];
  }
  return result;
}

function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

/** Read a local file URI (file://, blob:, data:, http(s)) into bytes. */
export async function readUriBytes(uri: string): Promise<Uint8Array> {
  if (uri.startsWith('data:')) {
    return decodeBase64(uri);
  }

  try {
    const response = await fetch(uri);
    if (response.ok) {
      return new Uint8Array(await response.arrayBuffer());
    }
  } catch {
    // Native file:// URIs sometimes need the FileSystem module.
  }

  const FileSystem = await import('expo-file-system/legacy');
  const base64 = await FileSystem.readAsStringAsync(uri, {
    encoding: FileSystem.EncodingType.Base64,
  });
  return decodeBase64(base64);
}

/** Persist bytes to a cache file (native) or an object URL (web). */
export async function writeCacheFile(bytes: Uint8Array, fileName: string, mimeType: string): Promise<string> {
  if (typeof URL !== 'undefined' && typeof Blob !== 'undefined' && typeof document !== 'undefined') {
    const blob = new Blob([toArrayBuffer(bytes)], { type: mimeType });
    return URL.createObjectURL(blob);
  }

  const FileSystem = await import('expo-file-system/legacy');
  const cacheDir = FileSystem.cacheDirectory;
  if (!cacheDir) {
    throw new Error('Cache directory is not available.');
  }
  const dest = `${cacheDir}${fileName}`;
  await FileSystem.writeAsStringAsync(dest, encodeBase64(bytes), {
    encoding: FileSystem.EncodingType.Base64,
  });
  return dest;
}
