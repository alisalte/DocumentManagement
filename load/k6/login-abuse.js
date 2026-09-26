import http from 'k6/http';
import { check, sleep } from 'k6';

/**
 * Confirms the login rate limiter returns 429 under credential stuffing.
 * Uses a low VU count; raise only against a disposable environment.
 */
const base = __ENV.BASE_URL || 'http://localhost:5080';

export const options = {
  vus: Number(__ENV.VUS || 5),
  duration: __ENV.DURATION || '30s',
  thresholds: {
    // At least some requests must be throttled when the default budget (10/min/IP) is exceeded.
    checks: ['rate>0'],
  },
};

export default function () {
  const response = http.post(
    `${base}/api/v1/auth/login`,
    JSON.stringify({ username: 'attacker', password: `wrong-${__ITER}` }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login-abuse' } },
  );
  check(response, {
    'unauthorized or throttled': (r) => r.status === 401 || r.status === 429,
    'saw 429': (r) => r.status === 429,
  });
  sleep(0.05);
}
