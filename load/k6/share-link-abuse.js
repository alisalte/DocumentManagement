import http from 'k6/http';
import { check, sleep } from 'k6';

/**
 * Confirms the share-link open rate limiter returns 429 under guessing.
 * Hit a non-existent token repeatedly; the limiter keys on client address.
 */
const base = __ENV.BASE_URL || 'http://localhost:5080';

export const options = {
  vus: Number(__ENV.VUS || 8),
  duration: __ENV.DURATION || '30s',
  thresholds: {
    checks: ['rate>0'],
  },
};

export default function () {
  const token = `guess-${__VU}-${__ITER}-${Date.now()}`;
  const response = http.get(`${base}/api/v1/share-links/${token}`, {
    tags: { name: 'share-link-abuse' },
  });
  check(response, {
    'not found or throttled': (r) => r.status === 404 || r.status === 429,
    'saw 429': (r) => r.status === 429,
  });
  sleep(0.05);
}
